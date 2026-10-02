import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { aiApi, API_BASE_URL, buildHeaders } from '@/api'
import type { ConfirmItem, RecognizedItem, RecognizeResponse } from '@/api'

/** 确认页可编辑的条目。 */
export interface EditableItem {
  index: number
  name: string
  portion: number
  estimatedCalories: number | null
  confidence: number
  needsReview: boolean
  matchedDishId: string | null
  matchedDishName: string | null
  conflictIngredients: string[]
  /** 用户是否改过 */
  edited: boolean
}

/** 上传图片（`uni.uploadFile` 不走请求封装器，需自己拼请求头）。 */
function uploadImage(filePath: string): Promise<{ url: string }> {
  return new Promise((resolve, reject) => {
    uni.uploadFile({
      url: `${API_BASE_URL}/api/v1/uploads/image`,
      filePath,
      name: 'file',
      header: buildHeaders(),
      success: (res) => {
        try {
          const body = JSON.parse(res.data) as { code: number; message: string; data: { url: string } }

          if (body.code !== 0) {
            reject(new Error(body.message || '上传失败'))
            return
          }

          resolve(body.data)
        } catch {
          reject(new Error('上传响应解析失败'))
        }
      },
      fail: (err) => reject(new Error(err.errMsg || '上传失败')),
    })
  })
}

export const useAiStore = defineStore('ai', () => {
  const recognition = ref<RecognizeResponse | null>(null)
  const imageUrl = ref<string | null>(null)

  const uploading = ref(false)
  const recognizing = ref(false)
  const confirming = ref(false)
  const error = ref<string | null>(null)

  /** 图片 + 识别都完成 */
  const hasResult = computed(() => (recognition.value?.items.length ?? 0) > 0)

  /** 需要复核的条目数 */
  const reviewCount = computed(
    () => recognition.value?.items.filter((i) => i.needsReview).length ?? 0,
  )

  /** 把识别结果转成可编辑列表；低置信度的排前面，引导优先修正 */
  const editableItems = computed<EditableItem[]>(() => {
    const items = recognition.value?.items ?? []

    return [...items]
      .sort((a, b) => {
        if (a.needsReview !== b.needsReview) return a.needsReview ? -1 : 1
        return a.confidence - b.confidence
      })
      .map((item: RecognizedItem) => ({
        index: item.index,
        name: item.name,
        portion: item.portion,
        estimatedCalories: item.estimatedCalories,
        confidence: item.confidence,
        needsReview: item.needsReview,
        matchedDishId: item.matchedDishId,
        matchedDishName: item.matchedDishName,
        conflictIngredients: [...item.conflictIngredients],
        edited: false,
      }))
  })

  /** 选图 → 上传 → 识别。 */
  async function pickAndRecognize() {
    error.value = null

    let filePath: string

    try {
      const chosen = await new Promise<UniApp.ChooseImageSuccessCallbackResult>((resolve, reject) => {
        uni.chooseImage({
          count: 1,
          sizeType: ['compressed'],
          sourceType: ['camera', 'album'],
          success: resolve,
          fail: (err) => reject(new Error(err.errMsg || '取消选择')),
        })
      })

      filePath = chosen.tempFilePaths[0]
    } catch (err) {
      // 用户取消不算错误
      const message = (err as Error).message
      if (message.includes('cancel')) return
      error.value = message
      return
    }

    uploading.value = true

    try {
      const uploaded = await uploadImage(filePath)
      imageUrl.value = uploaded.url
    } catch (err) {
      error.value = (err as Error).message
      uploading.value = false
      return
    } finally {
      uploading.value = false
    }

    recognizing.value = true

    try {
      recognition.value = await aiApi.recognize(imageUrl.value!)
    } catch (err) {
      // 识别失败 → 引导手动记录，绝不阻塞用户
      error.value = (err as Error).message
      recognition.value = null
    } finally {
      recognizing.value = false
    }
  }

  /** 确认识别结果并入库。 */
  async function confirm(
    items: EditableItem[],
    mealType: number,
    diningMode: number,
    force = false,
  ) {
    const logId = recognition.value?.logId
    if (!logId) throw new Error('没有可确认的识别结果')

    const payload: ConfirmItem[] = items.map((item) => ({
      index: item.index,
      dishId: item.matchedDishId,
      name: item.name.trim(),
      portion: item.portion,
      estimatedCalories: item.estimatedCalories,
    }))

    confirming.value = true

    try {
      return await aiApi.confirm(logId, { mealType, diningMode, items: payload, force })
    } finally {
      confirming.value = false
    }
  }

  function reset() {
    recognition.value = null
    imageUrl.value = null
    error.value = null
  }

  return {
    recognition,
    imageUrl,
    uploading,
    recognizing,
    confirming,
    error,
    hasResult,
    reviewCount,
    editableItems,
    pickAndRecognize,
    confirm,
    reset,
  }
})
