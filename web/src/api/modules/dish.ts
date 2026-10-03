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

/** 菜品食材。 */
export interface DishIngredient {
  name: string
  amount: string | null
  isCommonAllergen: boolean
}

/** 菜谱步骤。 */
export interface DishRecipeStep {
  order: number
  text: string
  durationMinutes: number | null
}

/** 菜谱。 */
export interface DishRecipe {
  servings: number
  cookMinutes: number
  difficulty: number
  difficultyLabel: string
  steps: DishRecipeStep[]
  tips: string | null
}

/** 菜品详情（比简要信息多出食材与菜谱）。 */
export interface DishDetail {
  id: string
  name: string
  aliases: string[]
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
  description: string | null
  tags: string[]
  mealTimeLabels: string[]
  ingredients: DishIngredient[]
  /** 菜品库未缓存菜谱时为 null，前端可调 AI 生成 */
  recipe: DishRecipe | null
  isBuiltin: boolean
  conflictIngredients: string[]
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

  /** 菜品详情（含食材与菜谱）。 */
  detail: (id: string) => request<DishDetail>({ url: `/api/v1/dishes/${id}` }),
}
