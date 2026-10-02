import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { authApi, clearTokens, getAccessToken, getRefreshToken, setTokens } from '@/api'
import type { AuthUser } from '@/api'
import { platform } from '@/platform'

/** 登录状态。 */
export type AuthStatus = 'unknown' | 'logging-in' | 'authenticated' | 'failed'

/**
 * 认证状态。
 *
 * 启动流程：有令牌就先验一次 `/auth/me`；没有或已失效则调 `platform.login()`
 * 拿各端凭证，换回令牌对。整个过程对用户无感。
 */
export const useAuthStore = defineStore('auth', () => {
  const user = ref<AuthUser | null>(null)
  const needsOnboarding = ref(false)
  const status = ref<AuthStatus>('unknown')
  const error = ref<string | null>(null)

  const isAuthenticated = computed(() => Boolean(user.value))
  const isLoggingIn = computed(() => status.value === 'logging-in')

  /** 用各端登录凭证换取令牌。 */
  async function login(): Promise<boolean> {
    status.value = 'logging-in'
    error.value = null

    try {
      const credentials = await platform.login()

      const response = await authApi.login({
        platform: platformCode(),
        code: credentials.code,
        nickname: credentials.nickname ?? null,
        avatarUrl: credentials.avatarUrl ?? null,
      })

      setTokens(response.accessToken, response.refreshToken)

      user.value = response.user
      needsOnboarding.value = response.needsOnboarding ?? false
      status.value = 'authenticated'

      return true
    } catch (err) {
      error.value = (err as Error).message
      status.value = 'failed'
      return false
    }
  }

  /** 拉取当前用户资料。 */
  async function loadMe(): Promise<boolean> {
    try {
      const me = await authApi.me()

      user.value = me.user
      needsOnboarding.value = me.needsOnboarding

      return true
    } catch {
      return false
    }
  }

  /**
   * 确保处于已登录状态。
   *
   * 有令牌时先验一次；失败再走一次完整登录。这样冷启动时多一次轻量请求，
   * 换来的是「令牌还有效就不必重新走各端授权」。
   */
  async function ensureLoggedIn(): Promise<boolean> {
    if (isAuthenticated.value) return true

    if (getAccessToken() || getRefreshToken()) {
      if (await loadMe()) {
        status.value = 'authenticated'
        return true
      }

      // 令牌彻底失效
      clearTokens()
    }

    return login()
  }

  /** 登出。 */
  async function logout(): Promise<void> {
    try {
      await authApi.logout(getRefreshToken())
    } catch {
      // 服务端撤销失败也要清本地，否则用户会卡在「登不出去」的状态
    }

    clearTokens()
    user.value = null
    needsOnboarding.value = false
    status.value = 'unknown'
  }

  /** 更新昵称与头像。 */
  async function updateProfile(nickname?: string, avatarUrl?: string): Promise<boolean> {
    try {
      const updated = await authApi.updateProfile({ nickname, avatarUrl })
      user.value = updated
      return true
    } catch (err) {
      error.value = (err as Error).message
      return false
    }
  }

  function markOnboardingCompleted(): void {
    needsOnboarding.value = false
  }

  return {
    user,
    needsOnboarding,
    status,
    error,
    isAuthenticated,
    isLoggingIn,
    login,
    loadMe,
    ensureLoggedIn,
    logout,
    updateProfile,
    markOnboardingCompleted,
  }
})

/** 平台适配器的名称 → 后端枚举值。 */
function platformCode(): number {
  switch (platform.name) {
    case 'wechat':
      return 1
    case 'alipay':
      return 2
    case 'douyin':
      return 3
    default:
      return 4
  }
}
