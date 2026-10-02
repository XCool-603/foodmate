import { request } from '../request'
import type { PagedResult } from '../types'

/** 菜品简要信息。 */
export interface DishBrief {
  id: string
  name: string
  cuisine: number
  cuisineLabel: string
  category: number
  categoryLabel: string
  spicyLevel: number
  spicyLabel: string
  priceMinCents: number | null
  priceMaxCents: number | null
  calories: number | null
  imageUrl: string | null
  tags: string[]
  hasRecipe: boolean
  cookMinutes: number | null
  isBuiltin: boolean
  popularity: number
}

/** 菜品列表查询条件。 */
export interface DishQuery {
  keyword?: string
  cuisine?: number
  category?: number
  page?: number
  pageSize?: number
}

export const dishApi = {
  /** 菜品列表（支持关键字与菜系筛选）。 */
  list: (query: DishQuery = {}) =>
    request<PagedResult<DishBrief>>({
      url: '/api/v1/dishes',
      data: query as unknown as Record<string, unknown>,
    }),

  /** 菜品详情。 */
  detail: (id: string) => request<DishBrief>({ url: `/api/v1/dishes/${id}` }),
}
