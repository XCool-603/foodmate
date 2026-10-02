import { request } from '../request'
import type { ProfileSummary } from './record'

/** 就餐方式偏好权重。 */
export interface DiningModeWeights {
  takeout: number
  dineIn: number
  homemade: number
}

/** 口味画像。 */
export interface Preference {
  spicyLevel: number
  budgetMinCents: number
  budgetMaxCents: number
  avoidIngredients: string[]
  preferredCuisines: number[]
  diningModeWeights: DiningModeWeights
  maxDistanceMeters: number
  onboardingCompleted: boolean
  updatedAt: string
}

/** 更新画像（字段可选）。 */
export interface UpdatePreferenceRequest {
  spicyLevel?: number
  budgetMinCents?: number
  budgetMaxCents?: number
  avoidIngredients?: string[]
  preferredCuisines?: number[]
  maxDistanceMeters?: number
}

/** 新用户引导提交。 */
export interface OnboardingRequest {
  spicyLevel: number
  budgetMinCents: number
  budgetMaxCents: number
  avoidIngredients: string[]
  preferredCuisines: number[]
  diningMode: number
}

/** 画像学习建议。 */
export interface PreferenceSuggestion {
  spicyLevel: number | null
  budgetMinCents: number | null
  budgetMaxCents: number | null
  preferredCuisines: number[] | null
  preferredCuisineLabels: string[] | null
  sampleSize: number
  hasAny: boolean
  requiredSampleSize: number
}

export const profileApi = {
  /** 获取口味画像。 */
  getPreference: () => request<Preference>({ url: '/api/v1/me/preference' }),

  /** 更新口味画像。 */
  updatePreference: (body: UpdatePreferenceRequest) =>
    request<Preference>({
      url: '/api/v1/me/preference',
      method: 'PUT',
      data: body as unknown as Record<string, unknown>,
    }),

  /** 完成新用户引导。 */
  completeOnboarding: (body: OnboardingRequest) =>
    request<Preference>({
      url: '/api/v1/me/preference/onboarding',
      method: 'POST',
      data: body as unknown as Record<string, unknown>,
    }),

  /** 取画像学习建议（只建议，不应用）。 */
  getSuggestion: () =>
    request<PreferenceSuggestion>({ url: '/api/v1/me/preference/suggestion' }),

  /** 应用画像建议。 */
  applySuggestion: () =>
    request<Preference>({
      url: '/api/v1/me/preference/apply-suggestion',
      method: 'POST',
    }),

  /** 「我的」页数据卡片。 */
  summary: () => request<ProfileSummary>({ url: '/api/v1/me/summary' }),
}
