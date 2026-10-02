/** 后端统一响应外层结构。 */
export interface ApiResponse<T> {
  /** 业务错误码；0 表示成功。 */
  code: number
  /** 面向开发者的描述。 */
  message: string
  /** 业务数据；失败时为 null。 */
  data: T | null
  /** 链路追踪 ID。 */
  traceId?: string | null
}

/** 分页结果。 */
export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
  hasMore: boolean
}

/** 业务错误码。与后端 `FoodMate.Core.Exceptions.ApiErrorCode` 保持一致。 */
export const ApiErrorCode = {
  Success: 0,

  Unauthorized: 1001,
  TokenExpired: 1002,
  Forbidden: 1003,
  UnsupportedPlatform: 1004,
  RefreshTokenInvalid: 1005,

  ValidationFailed: 2001,
  NotFound: 2002,
  AvoidIngredientConflict: 2003,
  RecordNotFound: 2004,
  DishAlreadyExists: 2005,

  AiRecognitionFailed: 3001,
  AiTimeout: 3002,
  AiQuotaExceeded: 3003,
  ImageFormatUnsupported: 3004,
  ImageTooLarge: 3005,
  AiParseFailed: 3006,

  NoCandidates: 4001,

  InternalError: 5000,
  DatabaseError: 5001,
  ThirdPartyUnavailable: 5002,
} as const

/** 统一的 API 异常。 */
export class ApiError extends Error {
  /** 业务错误码。 */
  readonly code: number
  /** HTTP 状态码。 */
  readonly httpStatus: number
  /** 链路追踪 ID，反馈问题时可提供给后端。 */
  readonly traceId?: string | null

  constructor(code: number, message: string, httpStatus = 200, traceId?: string | null) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.httpStatus = httpStatus
    this.traceId = traceId
  }

  /** 是否为网络层失败（而非业务失败）。 */
  get isNetworkError(): boolean {
    return this.httpStatus === 0
  }
}
