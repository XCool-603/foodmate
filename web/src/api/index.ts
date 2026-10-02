export * from './types'
export {
  request,
  API_BASE_URL,
  buildHeaders,
  getAccessToken,
  getRefreshToken,
  setTokens,
  clearTokens,
  refreshAccessToken,
} from './request'
export type { RequestOptions } from './request'

export { metaApi, healthApi } from './modules/meta'
export type { EnumDictionary, EnumOption, ServiceMeta, HealthInfo, ReadyInfo } from './modules/meta'

export { dishApi } from './modules/dish'
export type { DishBrief, DishQuery } from './modules/dish'

export { decisionApi, engineApi } from './modules/decision'
export type {
  DecisionSuggestRequest,
  DecisionSuggestResponse,
  ScoredDish,
  Penalty,
  EngineMeta,
} from './modules/decision'

export { recordApi } from './modules/record'
export type {
  RecordItem,
  RecordQuery,
  RecordStats,
  CreateRecordRequest,
  UpdateRecordRequest,
  PendingRating,
  ProfileSummary,
  DishSnapshot,
  DistributionItem,
} from './modules/record'

export { profileApi } from './modules/profile'
export type {
  Preference,
  UpdatePreferenceRequest,
  OnboardingRequest,
  PreferenceSuggestion,
  DiningModeWeights,
} from './modules/profile'

export { aiApi } from './modules/ai'
export type {
  RecognizeResponse,
  RecognizedItem,
  ConfirmItem,
  ConfirmResponse,
  Recipe,
  RecipeStep,
  ShoppingList,
} from './modules/ai'

export { authApi } from './modules/auth'
export type { AuthUser, AuthTokenResponse, MeResponse } from './modules/auth'
