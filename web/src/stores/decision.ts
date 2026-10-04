import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { decisionApi } from '@/api'
import type { ChoiceSource, DecisionSuggestRequest, DecisionSuggestResponse, ScoredDish } from '@/api'

/** 用户选择的来源。 */
export type { ChoiceSource }

/**
 * 决策状态。
 *
 * 结果页需要完整的推荐数据（含打分明细），不适合走页面参数传递，
 * 因此用 store 承载「最近一次决策」。
 */
export const useDecisionStore = defineStore('decision', () => {
  /** 最近一次请求参数，用于「换一批」与条件回填。 */
  const lastRequest = ref<DecisionSuggestRequest | null>(null)

  /** 最近一次决策结果。 */
  const result = ref<DecisionSuggestResponse | null>(null)

  const loading = ref(false)
  const error = ref<string | null>(null)

  /** 本次决策使用的抖动标准差。 */
  const jitterUsed = ref(0)

  const hasResult = computed(() => (result.value?.ranked.length ?? 0) > 0)

  /** 转盘候选：按 wheelPicks 的顺序取出对应菜品。 */
  const wheelItems = computed<ScoredDish[]>(() => {
    const data = result.value
    if (!data) return []

    const byId = new Map(data.ranked.map((item) => [item.dishId, item]))
    return data.wheelPicks
      .map((id) => byId.get(id))
      .filter((item): item is ScoredDish => Boolean(item))
  })

  /** 榜单默认展示的条目。 */
  const displayList = computed(() => result.value?.ranked.slice(0, 5) ?? [])

  /** 执行一次决策。 */
  async function suggest(request: DecisionSuggestRequest) {
    loading.value = true
    error.value = null

    try {
      const response = await decisionApi.suggest(request)
      lastRequest.value = request
      result.value = response
      jitterUsed.value = request.exploreJitter ?? 0
      return response
    } catch (err) {
      error.value = (err as Error).message
      result.value = null
      throw err
    } finally {
      loading.value = false
    }
  }

  /** 换一批：排除当前结果并加入抖动，让推荐有变化。 */
  async function refresh() {
    const request = lastRequest.value
    const data = result.value

    if (!request || !data) return null

    return suggest({
      ...request,
      excludeDishIds: data.ranked.map((item) => item.dishId),
      exploreJitter: 3,
    })
  }

  /** 回传用户选择。失败不阻塞主流程。 */
  async function choose(dishId: string, source: ChoiceSource) {
    const sessionId = result.value?.sessionId
    if (!sessionId) return

    try {
      await decisionApi.choose(sessionId, dishId, source)
    } catch {
      // 选择回传是「锦上添花」，失败不应打断用户
    }
  }

  function reset() {
    result.value = null
    lastRequest.value = null
    error.value = null
  }

  return {
    lastRequest,
    result,
    loading,
    error,
    jitterUsed,
    hasResult,
    wheelItems,
    displayList,
    suggest,
    refresh,
    choose,
    reset,
  }
})
