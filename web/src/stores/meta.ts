import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { healthApi, metaApi } from '@/api'
import type { EnumDictionary, HealthInfo, ReadyInfo } from '@/api'
import { platform } from '@/platform'

/** 后端连接状态。 */
export type ConnectionState = 'unknown' | 'checking' | 'online' | 'offline'

/**
 * 元信息与连接状态。
 *
 * M0 阶段的核心作用：让前端能自证「已打通后端」——
 * 页面直接展示 API 地址、服务版本、数据库连通性与菜品数量。
 */
export const useMetaStore = defineStore('meta', () => {
  const enums = ref<EnumDictionary | null>(null)
  const health = ref<HealthInfo | null>(null)
  const ready = ref<ReadyInfo | null>(null)

  const state = ref<ConnectionState>('unknown')
  const errorMessage = ref<string | null>(null)
  const lastCheckedAt = ref<number | null>(null)

  const isOnline = computed(() => state.value === 'online')
  const dishCount = computed(() => ready.value?.checks.dishCount ?? 0)
  const apiBaseUrl = computed(() => import.meta.env.VITE_API_BASE_URL ?? '')
  const platformLabel = computed(() => platform.label)

  /** 拉取枚举字典（带内存缓存）。 */
  async function loadEnums(force = false): Promise<EnumDictionary | null> {
    if (enums.value && !force) return enums.value
    try {
      enums.value = await metaApi.getEnums()
      return enums.value
    } catch (err) {
      errorMessage.value = (err as Error).message
      return null
    }
  }

  /** 探测后端连通性。 */
  async function checkConnection(): Promise<boolean> {
    state.value = 'checking'
    errorMessage.value = null

    try {
      health.value = await healthApi.check()
      ready.value = await healthApi.ready()
      state.value = 'online'
      lastCheckedAt.value = Date.now()
      return true
    } catch (err) {
      state.value = 'offline'
      health.value = null
      ready.value = null
      errorMessage.value = (err as Error).message
      lastCheckedAt.value = Date.now()
      return false
    }
  }

  return {
    enums,
    health,
    ready,
    state,
    errorMessage,
    lastCheckedAt,
    isOnline,
    dishCount,
    apiBaseUrl,
    platformLabel,
    loadEnums,
    checkConnection,
  }
})
