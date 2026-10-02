import { request } from '../request'

/** 枚举选项。 */
export interface EnumOption {
  value: number
  label: string
}

/** 枚举字典。前端启动时拉取并缓存，避免硬编码枚举值。 */
export interface EnumDictionary {
  platform: EnumOption[]
  mealType: EnumOption[]
  diningMode: EnumOption[]
  cuisine: EnumOption[]
  dishCategory: EnumOption[]
  spicyLevel: EnumOption[]
  recordSource: EnumOption[]
  moodTag: string[]
  avoidIngredient: string[]
}

/** 服务元信息。 */
export interface ServiceMeta {
  name: string
  version: string
  environment: string
  serverTime: string
}

/** 存活探针响应（裸结构，不套统一响应外层）。 */
export interface HealthInfo {
  status: string
  name: string
  version: string
  environment: string
  serverTime: string
}

/** 就绪探针响应（裸结构）。 */
export interface ReadyInfo {
  status: string
  checks: {
    database: string
    dishCount: number
  }
  serverTime: string
}

export const metaApi = {
  /** 获取枚举字典。 */
  getEnums: () => request<EnumDictionary>({ url: '/api/v1/meta/enums' }),

  /** 获取服务元信息。 */
  getInfo: () => request<ServiceMeta>({ url: '/api/v1/meta/info' }),
}

export const healthApi = {
  /** 存活探针。 */
  check: () => request<HealthInfo>({ url: '/health', raw: true, skipAuth: true, retries: 0 }),

  /** 就绪探针，含数据库连通性与菜品数量。 */
  ready: () => request<ReadyInfo>({ url: '/health/ready', raw: true, skipAuth: true, retries: 0 }),
}
