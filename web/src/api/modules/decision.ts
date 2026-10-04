import { request } from '../request'
import type { DishBrief } from './dish'

/** 决策请求。 */
export interface DecisionSuggestRequest {
  mealType?: number | null
  diningMode?: number
  partySize?: number
  budgetMinCents?: number | null
  budgetMaxCents?: number | null
  location?: { latitude: number; longitude: number } | null
  weather?: string | null
  moodTags?: string[]
  excludeDishIds?: string[]
  exploreJitter?: number
}

/** 已应用的乘性惩罚。 */
export interface Penalty {
  code: string
  multiplier: number
  description: string
}

/** 单条推荐。 */
export interface ScoredDish {
  rank: number
  dishId: string
  name: string
  score: number
  breakdown: Record<string, number>
  penalties: Penalty[]
  reasons: string[]
  dish: DishBrief
}

/** 决策响应。 */
export interface DecisionSuggestResponse {
  sessionId: string
  engineVersion: string
  elapsedMs: number
  candidateCount: number
  filteredOutCount: number
  relaxedLevel: number
  wheelPicks: string[]
  ranked: ScoredDish[]
}

/** 决策引擎元信息。 */
export interface EngineMeta {
  version: string
  weights: Record<string, number>
  dimensionLabels: Record<string, string>
}

/** 用户选择的来源，用于分析「哪种交互更容易被采纳」。 */
export type ChoiceSource = 'card' | 'wheel' | 'list' | 'other'

export const decisionApi = {
  /** 获取推荐。 */
  suggest: (body: DecisionSuggestRequest) =>
    request<DecisionSuggestResponse>({
      url: '/api/v1/decisions/suggest',
      method: 'POST',
      data: body as unknown as Record<string, unknown>,
    }),

  /** 回传用户最终选择（权重调优的核心数据）。 */
  choose: (sessionId: string, dishId: string, source: ChoiceSource = 'list') =>
    request<{ sessionId: string; dishId: string; rank: number; chosenAt: string }>({
      url: `/api/v1/decisions/${sessionId}/choose`,
      method: 'POST',
      data: { dishId, source },
    }),
}

export const engineApi = {
  /** 取引擎权重，用于把 breakdown 换算成贡献度。 */
  meta: () => request<EngineMeta>({ url: '/api/v1/meta/engine' }),
}
