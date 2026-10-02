import { request } from '../request'

/** 识别出的一道菜。 */
export interface RecognizedItem {
  index: number
  name: string
  confidence: number
  needsReview: boolean
  estimatedCalories: number | null
  ingredients: string[]
  portion: number
  matchedDishId: string | null
  matchedDishName: string | null
  matchScore: number | null
  matchedViaAlias: boolean
  conflictIngredients: string[]
}

/** 识别响应。 */
export interface RecognizeResponse {
  logId: string
  modelName: string
  /** 为 true 时结果与图片内容无关，仅供本地跑通流程 */
  isStubModel: boolean
  latencyMs: number
  scene: string
  sceneLabel: string
  overallConfidence: number
  items: RecognizedItem[]
  remainingToday: number
}

/** 确认时提交的单道菜。 */
export interface ConfirmItem {
  index: number
  dishId?: string | null
  name: string
  portion: number
  estimatedCalories?: number | null
}

/** 确认响应。 */
export interface ConfirmResponse {
  recordIds: string[]
  createdDishIds: string[]
  correctedCount: number
  wasCorrected: boolean
}

/** 菜谱步骤。 */
export interface RecipeStep {
  order: number
  text: string
  durationMinutes: number | null
}

/** 菜谱。 */
export interface Recipe {
  dishId: string | null
  dishName: string
  servings: number
  cookMinutes: number
  difficulty: number
  difficultyLabel: string
  steps: RecipeStep[]
  tips: string | null
  fromCache: boolean
  isStubModel: boolean
}

/** 买菜清单项。 */
export interface ShoppingItem {
  name: string
  totalAmount: string | null
  category: string | null
  forDishes: string[]
}

/** 买菜清单。 */
export interface ShoppingList {
  items: ShoppingItem[]
  textSummary: string
  isStubModel: boolean
}

export const aiApi = {
  /** 识别图片中的菜品（不入库）。 */
  recognize: (imageUrl: string) =>
    request<RecognizeResponse>({
      url: '/api/v1/ai/recognize',
      method: 'POST',
      data: { imageUrl },
      // AI 调用较慢，给足超时
      retries: 0,
    }),

  /** 确认（或修正）后写入记录。 */
  confirm: (
    logId: string,
    body: {
      mealType: number
      diningMode: number
      eatenAt?: string | null
      items: ConfirmItem[]
      force?: boolean
    },
  ) =>
    request<ConfirmResponse>({
      url: `/api/v1/ai/recognize/${logId}/confirm`,
      method: 'POST',
      data: body as unknown as Record<string, unknown>,
      idempotencyKey: `ai-confirm-${logId}`,
    }),

  /** 生成菜谱。 */
  recipe: (dishId: string | null, dishName: string, servings = 2) =>
    request<Recipe>({
      url: '/api/v1/ai/recipe',
      method: 'POST',
      data: { dishId, dishName, servings },
      retries: 0,
    }),

  /** 生成买菜清单。 */
  shoppingList: (dishIds: string[], servings = 2) =>
    request<ShoppingList>({
      url: '/api/v1/ai/shopping-list',
      method: 'POST',
      data: { dishIds, servings },
      retries: 0,
    }),
}
