import { platform } from '@/platform'
import { getDeviceId } from '@/utils/device'
import { ApiError, ApiErrorCode } from './types'
import type { ApiResponse } from './types'

const BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '')
const TIMEOUT = Number(import.meta.env.VITE_API_TIMEOUT ?? 15000)
const MAX_RETRIES = 2

const ACCESS_TOKEN_KEY = 'fm_access_token'
const REFRESH_TOKEN_KEY = 'fm_refresh_token'

/** API 基地址。`uni.uploadFile` 这类绕过封装器的调用需要自己拼。 */
export const API_BASE_URL = BASE_URL

/** 单次请求的参数。 */
export interface RequestOptions {
  /** 路径，如 `/api/v1/dishes`。 */
  url: string
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  /** GET 时作为查询参数，其余作为 JSON body。 */
  data?: Record<string, unknown>
  /** 幂等键，写操作必传（防移动端双击 / 重试产生重复数据）。 */
  idempotencyKey?: string
  /** 跳过 token 注入（登录、续期、健康检查等）。 */
  skipAuth?: boolean
  /** 直接返回响应体，不做统一结构解包（用于 `/health` 这类裸结构端点）。 */
  raw?: boolean
  /** 覆盖默认重试次数。 */
  retries?: number
  /** 内部使用：标记「已续期重试过」，避免无限递归。 */
  retriedAfterRefresh?: boolean
}

// ── 令牌读写 ────────────────────────────────────────────────

/** 读取访问令牌。 */
export function getAccessToken(): string {
  try {
    return (uni.getStorageSync(ACCESS_TOKEN_KEY) as string) ?? ''
  } catch {
    return ''
  }
}

/** 读取刷新令牌。 */
export function getRefreshToken(): string {
  try {
    return (uni.getStorageSync(REFRESH_TOKEN_KEY) as string) ?? ''
  } catch {
    return ''
  }
}

/** 写入令牌对。 */
export function setTokens(accessToken: string, refreshToken: string): void {
  try {
    uni.setStorageSync(ACCESS_TOKEN_KEY, accessToken)
    uni.setStorageSync(REFRESH_TOKEN_KEY, refreshToken)
  } catch {
    // Storage 不可用时退化为内存态，本次会话仍可用
  }
}

/** 清空令牌。 */
export function clearTokens(): void {
  try {
    uni.removeStorageSync(ACCESS_TOKEN_KEY)
    uni.removeStorageSync(REFRESH_TOKEN_KEY)
  } catch {
    // 忽略
  }
}

/** 统一的请求头（令牌 + 设备 ID + 平台）。 */
export function buildHeaders(extra?: Record<string, string>): Record<string, string> {
  return {
    'Content-Type': 'application/json',
    'X-Device-Id': getDeviceId(),
    'X-Platform': platform.name,
    ...extra,
  }
}

// ── 令牌续期 ────────────────────────────────────────────────

let refreshing: Promise<boolean> | null = null

/**
 * 续期访问令牌。
 *
 * 并发去重：多个请求同时收到 401 时只会真正续期一次，其余等同一个 Promise。
 * 否则会同时发出 N 个续期请求，而服务端采用令牌轮换——
 * 第一个成功、其余全被判为重放，导致用户被强制登出。
 */
export function refreshAccessToken(): Promise<boolean> {
  if (refreshing) return refreshing

  refreshing = (async () => {
    const refreshToken = getRefreshToken()
    if (!refreshToken) return false

    try {
      const response = await uni.request({
        url: `${BASE_URL}/api/v1/auth/refresh`,
        method: 'POST',
        data: { refreshToken },
        header: buildHeaders(),
        timeout: TIMEOUT,
      })

      const body = response.data as ApiResponse<{ accessToken: string; refreshToken: string }>

      if (response.statusCode !== 200 || body?.code !== 0 || !body.data) {
        return false
      }

      setTokens(body.data.accessToken, body.data.refreshToken)
      return true
    } catch {
      return false
    } finally {
      refreshing = null
    }
  })()

  return refreshing
}

// ── 请求 ────────────────────────────────────────────────────

/** 底层请求：不做重试。 */
async function send(options: RequestOptions): Promise<{ status: number; body: unknown }> {
  const token = getAccessToken()

  const header: Record<string, string> = buildHeaders()

  if (!options.skipAuth && token) {
    header.Authorization = `Bearer ${token}`
  }
  if (options.idempotencyKey) {
    header['Idempotency-Key'] = options.idempotencyKey
  }

  const res = (await uni.request({
    url: BASE_URL + options.url,
    method: options.method ?? 'GET',
    data: options.data,
    header,
    timeout: TIMEOUT,
  })) as unknown as { statusCode: number; data: unknown }

  return { status: res.statusCode, body: res.data }
}

function isRetryable(status: number): boolean {
  return status === 0 || status >= 500
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/**
 * 发起请求。
 *
 * - 自动注入 JWT 与幂等键
 * - 401 时自动续期一次并重放原请求
 * - 网络失败 / 5xx 指数退避重试（300ms → 900ms）
 * - 业务错误抛出 {@link ApiError}
 */
export async function request<T>(options: RequestOptions): Promise<T> {
  const maxRetries = options.retries ?? MAX_RETRIES
  let lastError: unknown

  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    try {
      const { status, body } = await send(options)

      // ── 401：续期一次后重放 ──────────────────────────
      if (status === 401 && !options.skipAuth && !options.retriedAfterRefresh) {
        const refreshed = await refreshAccessToken()

        if (refreshed) {
          return request<T>({ ...options, retriedAfterRefresh: true, retries: 0 })
        }

        clearTokens()
        throw new ApiError(ApiErrorCode.RefreshTokenInvalid, '登录已过期，请重新登录', 401)
      }

      // ── 限流：不重试 ─────────────────────────────────
      if (status === 429) {
        throw new ApiError(ApiErrorCode.AiQuotaExceeded, '请求过于频繁，请稍后再试', 429)
      }

      // ── 5xx：可重试 ──────────────────────────────────
      if (status >= 500) {
        lastError = new ApiError(ApiErrorCode.InternalError, '服务开小差了，请稍后再试', status)

        if (attempt < maxRetries) {
          await delay(300 * Math.pow(3, attempt))
          continue
        }

        throw lastError
      }

      // ── 裸结构端点（/health）─────────────────────────
      if (options.raw) {
        return body as T
      }

      const envelope = body as ApiResponse<T>

      if (!envelope || typeof envelope.code !== 'number') {
        throw new ApiError(ApiErrorCode.InternalError, '响应格式异常', status)
      }

      if (envelope.code !== ApiErrorCode.Success) {
        throw new ApiError(envelope.code, envelope.message, status, envelope.traceId)
      }

      return envelope.data as T
    } catch (err) {
      if (err instanceof ApiError && !err.isNetworkError && err.httpStatus < 500) {
        throw err
      }

      lastError = err

      if (attempt < maxRetries) {
        await delay(300 * Math.pow(3, attempt))
        continue
      }

      break
    }
  }

  if (lastError instanceof ApiError) throw lastError

  throw new ApiError(ApiErrorCode.InternalError, '网络连接失败，请检查网络后重试', 0)
}
