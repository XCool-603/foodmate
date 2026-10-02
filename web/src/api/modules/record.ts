import { request } from '../request'
import type { PagedResult } from '../types'

/** 记录中的菜品快照。 */
export interface DishSnapshot {
  cuisine: number
  cuisineLabel: string
  category: number
  categoryLabel: string
  spicyLevel: number
  caloriesPerServing: number | null
  priceCents: number | null
  tags: string[]
}

/** 饮食记录。 */
export interface RecordItem {
  id: string
  dishId: string | null
  dishName: string
  dishSnapshot: DishSnapshot
  mealType: number
  mealTypeLabel: string
  diningMode: number
  diningModeLabel: string
  eatenAt: string
  servings: number
  calories: number | null
  rating: number | null
  wouldEatAgain: boolean | null
  photoUrl: string | null
  note: string | null
  source: number
  sourceLabel: string
}

/** 创建记录。 */
export interface CreateRecordRequest {
  dishId?: string | null
  dishName?: string | null
  mealType: number
  diningMode: number
  eatenAt?: string | null
  servings?: number
  rating?: number | null
  wouldEatAgain?: boolean | null
  photoUrl?: string | null
  note?: string | null
  source?: number
  decisionSessionId?: string | null
  force?: boolean
}

/** 更新记录（字段可选）。 */
export interface UpdateRecordRequest {
  mealType?: number
  diningMode?: number
  eatenAt?: string
  servings?: number
  rating?: number
  wouldEatAgain?: boolean
  note?: string
  photoUrl?: string
}

/** 记录查询条件。 */
export interface RecordQuery {
  page?: number
  pageSize?: number
  from?: string
  to?: string
  mealType?: number
  dishId?: string
  hasRating?: boolean
}

/** 分布项。 */
export interface DistributionItem {
  value: number
  label: string
  count: number
}

/** 单日热量。 */
export interface DailyCalories {
  date: string
  calories: number
}

/** 统计报表。 */
export interface RecordStats {
  from: string
  to: string
  totalRecords: number
  totalCalories: number
  averageCaloriesPerMeal: number
  averageRating: number | null
  recordedDays: number
  newDishesTried: number
  mealTypeDistribution: DistributionItem[]
  cuisineDistribution: DistributionItem[]
  diningModeDistribution: DistributionItem[]
  dailyCalories: DailyCalories[]
}

/** 待评分提醒项。 */
export interface PendingRating {
  id: string
  dishName: string
  photoUrl: string | null
  eatenAt: string
  daysAgo: number
}

/** 「我的」页数据卡片。 */
export interface ProfileSummary {
  totalRecords: number
  totalDishesTried: number
  currentStreakDays: number
  longestStreakDays: number
  thisWeekRecords: number
  thisWeekNewDishes: number
  favoriteCuisine: DistributionItem | null
  averageRating: number | null
}

/** 把查询条件转成 uni.request 需要的扁平对象。 */
function toQuery(query: RecordQuery): Record<string, unknown> {
  const result: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null) result[key] = value
  }
  return result
}

export const recordApi = {
  /** 创建记录。 */
  create: (body: CreateRecordRequest, idempotencyKey?: string) =>
    request<RecordItem>({
      url: '/api/v1/records',
      method: 'POST',
      data: body as unknown as Record<string, unknown>,
      idempotencyKey,
    }),

  /** 记录列表（按就餐时间倒序）。 */
  list: (query: RecordQuery = {}) =>
    request<PagedResult<RecordItem>>({
      url: '/api/v1/records',
      data: toQuery(query),
    }),

  /** 记录详情。 */
  detail: (id: string) => request<RecordItem>({ url: `/api/v1/records/${id}` }),

  /** 更新记录。 */
  update: (id: string, body: UpdateRecordRequest) =>
    request<RecordItem>({
      url: `/api/v1/records/${id}`,
      method: 'PUT',
      data: body as unknown as Record<string, unknown>,
    }),

  /** 快速评分。 */
  rate: (id: string, rating: number, wouldEatAgain?: boolean) =>
    request<RecordItem>({
      url: `/api/v1/records/${id}/rating`,
      method: 'POST',
      data: { rating, wouldEatAgain },
    }),

  /** 删除记录。 */
  remove: (id: string) =>
    request<{ id: string; deleted: boolean }>({
      url: `/api/v1/records/${id}`,
      method: 'DELETE',
    }),

  /** 待评分提醒。 */
  pendingRating: (limit = 3) =>
    request<PendingRating[]>({
      url: '/api/v1/records/pending-rating',
      data: { limit },
    }),

  /** 统计报表。 */
  stats: (from?: string, to?: string) =>
    request<RecordStats>({
      url: '/api/v1/records/stats',
      data: toQuery({ from, to }),
    }),
}
