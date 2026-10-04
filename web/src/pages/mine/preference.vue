<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { profileApi } from '@/api'
import type { PreferenceSuggestion } from '@/api'
import { useMetaStore } from '@/stores'
import { formatPriceRange } from '@/utils/format'

const meta = useMetaStore()

const loading = ref(true)
const saving = ref(false)

const spicyLevel = ref(2)
const budgetMin = ref(1500)
const budgetMax = ref(5000)
const maxDistance = ref(2000)
const avoidIngredients = ref<string[]>([])
const preferredCuisines = ref<number[]>([])
const onboardingCompleted = ref(false)

const suggestion = ref<PreferenceSuggestion | null>(null)
const applyingSuggestion = ref(false)
const customAvoid = ref('')

const spicyOptions = computed(() => meta.enums?.spicyLevel ?? [])
const cuisineOptions = computed(() => meta.enums?.cuisine ?? [])
const allergenOptions = computed(() => meta.enums?.avoidIngredient ?? [])

const budgetText = computed(() => formatPriceRange(budgetMin.value, budgetMax.value))

const distanceText = computed(() => {
  const meters = maxDistance.value
  return meters >= 1000 ? `${(meters / 1000).toFixed(1)} 公里` : `${meters} 米`
})

async function load() {
  loading.value = true

  try {
    void meta.loadEnums()

    const preference = await profileApi.getPreference()

    spicyLevel.value = preference.spicyLevel
    budgetMin.value = preference.budgetMinCents
    budgetMax.value = preference.budgetMaxCents
    maxDistance.value = preference.maxDistanceMeters
    avoidIngredients.value = [...preference.avoidIngredients]
    preferredCuisines.value = [...preference.preferredCuisines]
    onboardingCompleted.value = preference.onboardingCompleted
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    loading.value = false
  }

  // 学习建议是「锦上添花」，失败不影响主流程
  try {
    suggestion.value = await profileApi.getSuggestion()
  } catch {
    suggestion.value = null
  }
}

function toggleAvoid(item: string) {
  const index = avoidIngredients.value.indexOf(item)
  if (index >= 0) avoidIngredients.value.splice(index, 1)
  else avoidIngredients.value.push(item)
}

function addCustomAvoid() {
  const word = customAvoid.value.trim()
  if (!word) return

  if (avoidIngredients.value.includes(word)) {
    uni.showToast({ title: '已经加过了', icon: 'none' })
    return
  }

  if (word.length > 10) {
    uni.showToast({ title: '单个词不能超过 10 个字', icon: 'none' })
    return
  }

  avoidIngredients.value.push(word)
  customAvoid.value = ''
}

function removeAvoid(item: string) {
  avoidIngredients.value = avoidIngredients.value.filter((x) => x !== item)
}

function toggleCuisine(value: number) {
  const index = preferredCuisines.value.indexOf(value)
  if (index >= 0) preferredCuisines.value.splice(index, 1)
  else preferredCuisines.value.push(value)
}

function onBudgetMin(e: { detail: { value: number } }) {
  budgetMin.value = e.detail.value
  if (budgetMin.value > budgetMax.value) budgetMax.value = budgetMin.value
}

function onBudgetMax(e: { detail: { value: number } }) {
  budgetMax.value = e.detail.value
  if (budgetMax.value < budgetMin.value) budgetMin.value = budgetMax.value
}

async function onSave() {
  saving.value = true

  try {
    await profileApi.updatePreference({
      spicyLevel: spicyLevel.value,
      budgetMinCents: budgetMin.value,
      budgetMaxCents: budgetMax.value,
      avoidIngredients: [...avoidIngredients.value],
      preferredCuisines: [...preferredCuisines.value],
      maxDistanceMeters: maxDistance.value,
    })

    uni.showToast({ title: '已保存，推荐会相应调整', icon: 'none' })
    setTimeout(() => uni.navigateBack(), 800)
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    saving.value = false
  }
}

async function onApplySuggestion() {
  applyingSuggestion.value = true

  try {
    const preference = await profileApi.applySuggestion()

    spicyLevel.value = preference.spicyLevel
    budgetMin.value = preference.budgetMinCents
    budgetMax.value = preference.budgetMaxCents
    preferredCuisines.value = [...preference.preferredCuisines]
    suggestion.value = null

    uni.showToast({ title: '已应用建议', icon: 'none' })
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    applyingSuggestion.value = false
  }
}

onShow(() => {
  void load()
})
</script>

<template>
  <view class="page">
    <view v-if="loading" class="empty">加载中…</view>

    <template v-else>
      <!-- 学习建议 -->
      <view v-if="suggestion?.hasAny" class="suggestion">
        <text class="suggestion__title">根据你最近的记录</text>
        <view class="suggestion__body">
          <text v-if="suggestion.spicyLevel !== null" class="suggestion__line">
            辣度偏好大约是「{{ spicyOptions.find((s) => s.value === suggestion!.spicyLevel)?.label ?? suggestion.spicyLevel }}」
          </text>
          <text v-if="suggestion.budgetMinCents" class="suggestion__line">
            常吃价位在 {{ formatPriceRange(suggestion.budgetMinCents, suggestion.budgetMaxCents) }}
          </text>
          <text v-if="suggestion.preferredCuisineLabels?.length" class="suggestion__line">
            常吃菜系：{{ suggestion.preferredCuisineLabels.join('、') }}
          </text>
          <text class="suggestion__sample">基于最近 {{ suggestion.sampleSize }} 条记录</text>
        </view>
        <view
          class="suggestion__apply"
          :class="{ 'suggestion__apply--busy': applyingSuggestion }"
          @tap="onApplySuggestion"
        >
          {{ applyingSuggestion ? '应用中…' : '一键应用' }}
        </view>
      </view>

      <view v-else-if="suggestion && !suggestion.hasAny" class="suggestion suggestion--muted">
        <text class="suggestion__line">
          记录满 {{ suggestion.requiredSampleSize }} 顿后，这里会显示根据你的历史学到的口味建议。
        </text>
      </view>

      <!-- 辣度 -->
      <view class="fm-card">
        <view class="section-head">
          <text class="section-title">能吃多辣</text>
          <text class="section-value">
            {{ spicyOptions.find((s) => s.value === spicyLevel)?.label ?? '—' }}
          </text>
        </view>
        <slider
          :value="spicyLevel"
          :min="0"
          :max="5"
          :step="1"
          activeColor="#FF6B35"
          block-size="20"
          @change="(e: any) => (spicyLevel = e.detail.value)"
        />
        <text class="hint">
          选「不辣」后，中辣及以上的菜会几乎被排除在推荐之外。
        </text>
      </view>

      <!-- 预算 -->
      <view class="fm-card">
        <view class="section-head">
          <text class="section-title">每餐预算</text>
          <text class="section-value">{{ budgetText }}</text>
        </view>
        <view class="slider-row">
          <text class="slider-row__label">下限</text>
          <slider
            class="slider-row__slider"
            :value="budgetMin"
            :min="0"
            :max="20000"
            :step="500"
            activeColor="#FF6B35"
            block-size="20"
            @change="onBudgetMin"
          />
        </view>
        <view class="slider-row">
          <text class="slider-row__label">上限</text>
          <slider
            class="slider-row__slider"
            :value="budgetMax"
            :min="0"
            :max="20000"
            :step="500"
            activeColor="#FF6B35"
            block-size="20"
            @change="onBudgetMax"
          />
        </view>
      </view>

      <!-- 忌口 -->
      <view class="fm-card">
        <view class="section-head">
          <text class="section-title">忌口 / 过敏</text>
          <text class="hint">硬性排除</text>
        </view>
        <text class="hint hint--block">
          这里勾选的食材，含它的菜品<b>完全不会出现在推荐里</b>——不是分数低，是直接排除。
        </text>

        <view class="fm-chips">
          <view
            v-for="item in allergenOptions"
            :key="item"
            class="fm-chip"
            :class="{ 'fm-chip--active': avoidIngredients.includes(item) }"
            @tap="toggleAvoid(item)"
          >
            {{ item }}
          </view>
        </view>

        <view v-if="avoidIngredients.filter((x) => !allergenOptions.includes(x)).length" class="custom-list">
          <view
            v-for="item in avoidIngredients.filter((x) => !allergenOptions.includes(x))"
            :key="item"
            class="custom-tag"
            @tap="removeAvoid(item)"
          >
            {{ item }} ✕
          </view>
        </view>

        <view class="custom-input">
          <input
            v-model="customAvoid"
            class="input"
            placeholder="其他忌口，如「香菜」"
            placeholder-class="input__placeholder"
            confirm-type="done"
            @confirm="addCustomAvoid"
          />
          <view class="custom-input__button" @tap="addCustomAvoid">添加</view>
        </view>
      </view>

      <!-- 偏好菜系 -->
      <view class="fm-card">
        <view class="section-head">
          <text class="section-title">爱吃哪些菜系</text>
          <text class="hint">可多选</text>
        </view>
        <view class="fm-chips">
          <view
            v-for="item in cuisineOptions"
            :key="item.value"
            class="fm-chip"
            :class="{ 'fm-chip--active': preferredCuisines.includes(item.value) }"
            @tap="toggleCuisine(item.value)"
          >
            {{ item.label }}
          </view>
        </view>
      </view>

      <!-- 距离 -->
      <view class="fm-card">
        <view class="section-head">
          <text class="section-title">能接受多远</text>
          <text class="section-value">{{ distanceText }}</text>
        </view>
        <slider
          :value="maxDistance"
          :min="100"
          :max="10000"
          :step="100"
          activeColor="#FF6B35"
          block-size="20"
          @change="(e: any) => (maxDistance = e.detail.value)"
        />
      </view>

      <view
        class="fm-button"
        :class="{ 'fm-button--disabled': saving }"
        @tap="onSave"
      >
        {{ saving ? '保存中…' : '保存画像' }}
      </view>

      <view class="footer">
        修改忌口会立即影响后续推荐，已产生的历史记录不受影响。
      </view>
    </template>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 建议卡 ── */
.suggestion {
  background: $fm-primary-soft;
  border-radius: $fm-radius-lg;
  padding: $fm-gap-lg;
  margin-bottom: $fm-gap-md;

  &--muted {
    background: $fm-bg-card;
  }

  &__title {
    font-size: 28rpx;
    font-weight: 600;
    color: $fm-primary;
  }

  &__body {
    margin-top: $fm-gap-sm;
  }

  &__line {
    display: block;
    font-size: 25rpx;
    color: $fm-text-secondary;
    line-height: 1.7;
  }

  &__sample {
    display: block;
    margin-top: 6rpx;
    font-size: 21rpx;
    color: $fm-text-tertiary;
  }

  &__apply {
    margin-top: $fm-gap-md;
    padding: 20rpx;
    text-align: center;
    background: rgba(0, 240, 255, 0.14);
    border: 1px solid $cy-cyan;
    color: $cy-cyan;
    font-size: 27rpx;
    font-weight: 700;
    letter-spacing: 2rpx;
    box-shadow: 0 0 16rpx rgba(0, 240, 255, 0.35);

    &--busy {
      background: $cy-surface-2;
      border-color: $cy-line;
      color: $cy-text-faint;
      box-shadow: none;
    }
  }
}

/* ── 通用 ── */
.section-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: $fm-gap-md;
}

.section-title {
  font-size: 30rpx;
  font-weight: 600;
}

.section-value {
  font-size: 28rpx;
  font-weight: 600;
  color: $fm-primary;
}

.hint {
  font-size: 22rpx;
  color: $fm-text-tertiary;

  &--block {
    display: block;
    margin-bottom: $fm-gap-md;
    line-height: 1.6;
  }
}

.slider-row {
  display: flex;
  align-items: center;

  & + & {
    margin-top: 8rpx;
  }

  &__label {
    width: 80rpx;
    font-size: 26rpx;
    color: $fm-text-secondary;
  }

  &__slider {
    flex: 1;
    margin: 0;
  }
}

/* ── 自定义忌口 ── */
.custom-list {
  display: flex;
  flex-wrap: wrap;
  gap: $fm-gap-sm;
  margin-top: $fm-gap-md;
}

.custom-tag {
  padding: 10rpx 24rpx;
  font-family: $cy-mono;
  font-size: 23rpx;
  background: rgba(255, 59, 92, 0.1);
  border: 1px solid rgba(255, 59, 92, 0.4);
  color: $cy-red;
}

.custom-input {
  display: flex;
  align-items: center;
  margin-top: $fm-gap-md;
  border-bottom: 2rpx solid $fm-border;

  &__button {
    font-size: 26rpx;
    color: $fm-primary;
    font-weight: 600;
    padding: 12rpx 0 12rpx 20rpx;
  }
}

.input {
  flex: 1;
  height: 76rpx;
  font-size: 27rpx;

  &__placeholder {
    color: $fm-text-tertiary;
  }
}

.empty {
  padding: 80rpx 0;
  text-align: center;
  font-size: 26rpx;
  color: $fm-text-tertiary;
}

.footer {
  margin-top: $fm-gap-lg;
  text-align: center;
  font-size: 21rpx;
  color: $fm-text-tertiary;
  line-height: 1.7;
}
</style>
