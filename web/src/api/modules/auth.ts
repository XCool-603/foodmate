import { request } from '../request'

/** 登录用户信息。 */
export interface AuthUser {
  id: string
  nickname: string | null
  avatarUrl: string | null
  platform: number
  platformLabel: string
  createdAt: string
}

/** 登录 / 续期响应。 */
export interface AuthTokenResponse {
  accessToken: string
  refreshToken: string
  expiresIn: number
  isNewUser?: boolean
  needsOnboarding?: boolean
  user: AuthUser | null
}

/** 当前用户信息。 */
export interface MeResponse {
  user: AuthUser
  needsOnboarding: boolean
  linkedPlatforms: number[]
}

export const authApi = {
  /** 三端统一登录。 */
  login: (body: { platform: number; code: string; nickname?: string | null; avatarUrl?: string | null }) =>
    request<AuthTokenResponse>({
      url: '/api/v1/auth/login',
      method: 'POST',
      data: body as unknown as Record<string, unknown>,
      skipAuth: true,
      retries: 1,
    }),

  /** 刷新令牌。 */
  refresh: (refreshToken: string) =>
    request<AuthTokenResponse>({
      url: '/api/v1/auth/refresh',
      method: 'POST',
      data: { refreshToken },
      skipAuth: true,
      retries: 0,
    }),

  /** 登出。 */
  logout: (refreshToken?: string | null) =>
    request<{ loggedOut: boolean }>({
      url: '/api/v1/auth/logout',
      method: 'POST',
      data: { refreshToken },
      retries: 0,
    }),

  /** 当前用户。 */
  me: () => request<MeResponse>({ url: '/api/v1/auth/me', retries: 0 }),

  /** 更新昵称与头像。 */
  updateProfile: (body: { nickname?: string; avatarUrl?: string }) =>
    request<AuthUser>({
      url: '/api/v1/auth/me',
      method: 'PUT',
      data: body as unknown as Record<string, unknown>,
    }),
}
