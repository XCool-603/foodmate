import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { recordApi } from '@/api'
import type {
  CreateRecordRequest,
  PendingRating,
  RecordItem,
  RecordStats,
  UpdateRecordRequest,
} from '@/api'

/** 按本地日期分组的记录。 */
export interface RecordGroup {
  /** yyyy-MM-dd */
  date: string
  /** 「今天」「昨天」或「6月15日 周日」 */
  label: string
  items: RecordItem[]
}

const PAGE_SIZE = 20

/** 取本地日期键。 */
export function localDateKey(input: string | Date): string {
  const d = typeof input === 'string' ? new Date(input) : input
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** 把日期键转成友好标签。 */
export function friendlyDate(key: string): string {
  const today = localDateKey(new Date())
  const yesterday = localDateKey(new Date(Date.now() - 86_400_000))

  if (key === today) return '今天'
  if (key === yesterday) return '昨天'

  const [, month, day] = key.split('-')
  return `${Number(month)}月${Number(day)}日`
}

export const useRecordStore = defineStore('record', () => {
  const items = ref<RecordItem[]>([])
  const total = ref(0)
  const page = ref(1)
  const loading = ref(false)
  const loadingMore = ref(false)
  const error = ref<string | null>(null)

  const stats = ref<RecordStats | null>(null)
  const pending = ref<PendingRating[]>([])

  const hasMore = computed(() => items.value.length < total.value)

  /** 按本地日期分组（后端只保证按时间倒序，分组交给前端）。 */
  const grouped = computed<RecordGroup[]>(() => {
    const map = new Map<string, RecordItem[]>()

    for (const item of items.value) {
      const key = localDateKey(item.eatenAt)
      const bucket = map.get(key)
      if (bucket) bucket.push(item)
      else map.set(key, [item])
    }

    return [...map.entries()]
      .sort((a, b) => (a[0] < b[0] ? 1 : -1))
      .map(([date, list]) => ({ date, label: friendlyDate(date), items: list }))
  })

  /** 加载第一页。 */
  async function load() {
    loading.value = true
    error.value = null

    try {
      const result = await recordApi.list({ page: 1, pageSize: PAGE_SIZE })
      items.value = result.items
      total.value = result.total
      page.value = 1
    } catch (err) {
      error.value = (err as Error).message
      items.value = []
      total.value = 0
    } finally {
      loading.value = false
    }
  }

  /** 上拉加载更多。 */
  async function loadMore() {
    if (loadingMore.value || !hasMore.value) return

    loadingMore.value = true

    try {
      const next = page.value + 1
      const result = await recordApi.list({ page: next, pageSize: PAGE_SIZE })
      items.value = [...items.value, ...result.items]
      total.value = result.total
      page.value = next
    } catch (err) {
      error.value = (err as Error).message
    } finally {
      loadingMore.value = false
    }
  }

  async function loadStats(from?: string, to?: string) {
    try {
      stats.value = await recordApi.stats(from, to)
    } catch {
      stats.value = null
    }
  }

  async function loadPending(limit = 3) {
    try {
      pending.value = await recordApi.pendingRating(limit)
    } catch {
      pending.value = []
    }
  }

  /** 创建记录；冲突时（2003）返回冲突标记供页面二次确认。 */
  async function create(
    body: CreateRecordRequest,
  ): Promise<{ ok: true; record: RecordItem } | { ok: false; conflict: string }> {
    try {
      const record = await recordApi.create(body, `rec-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`)
      items.value = [record, ...items.value]
      total.value += 1
      return { ok: true, record }
    } catch (err) {
      const apiError = err as { code?: number; message?: string }

      if (apiError.code === 2003) {
        return { ok: false, conflict: apiError.message ?? '含忌口食材' }
      }

      throw err
    }
  }

  /** 更新记录。 */
  async function update(id: string, body: UpdateRecordRequest) {
    const updated = await recordApi.update(id, body)
    replaceLocal(updated)
    return updated
  }

  /** 快速评分。 */
  async function rate(id: string, rating: number, wouldEatAgain?: boolean) {
    const updated = await recordApi.rate(id, rating, wouldEatAgain)
    replaceLocal(updated)
    pending.value = pending.value.filter((p) => p.id !== id)
    return updated
  }

  /** 删除记录。 */
  async function remove(id: string) {
    await recordApi.remove(id)
    items.value = items.value.filter((item) => item.id !== id)
    total.value = Math.max(0, total.value - 1)
  }

  function replaceLocal(updated: RecordItem) {
    const index = items.value.findIndex((item) => item.id === updated.id)
    if (index >= 0) items.value[index] = updated
  }

  function reset() {
    items.value = []
    total.value = 0
    page.value = 1
    stats.value = null
    pending.value = []
    error.value = null
  }

  return {
    items,
    total,
    page,
    loading,
    loadingMore,
    error,
    stats,
    pending,
    hasMore,
    grouped,
    load,
    loadMore,
    loadStats,
    loadPending,
    create,
    update,
    rate,
    remove,
    reset,
  }
})
