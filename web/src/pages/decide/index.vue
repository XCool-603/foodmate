<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { dishApi } from '@/api'
import type { DishBrief } from '@/api'
import { useDecisionStore, useMetaStore } from '@/stores'
import { formatPriceRange, greeting, mealPrompt } from '@/utils/format'

const meta = useMetaStore()
const decision = useDecisionStore()
const deciding = ref(false)

/* ── 决策条件（M1 将把这些参数提交给决策引擎）── */
const diningMode = ref(0)
const budgetMin = ref(1500)
const budgetMax = ref(5000)
const partySize = ref(1)
const selectedMoods = ref<string[]>([])

const greetingText = computed(() => greeting())
const promptText = computed(() => mealPrompt())
const budgetText = computed(
  () => `${formatPriceRange(budgetMin.value, budgetMax.value)}`,
)

const diningModes = computed(() => meta.enums?.diningMode ?? [
  { value: 0, label: '随便' },
  { value: 1, label: '外卖' },
  { value: 2, label: '堂食' },
  { value: 3, label: '自己做' },
])

const moodTags = computed(() => meta.enums?.moodTag ?? [])

const partyOptions = [
  { value: 1, label: '1 人' },
  { value: 2, label: '2 人' },
  { value: 3, label: '3–4 人' },
  { value: 5, label: '5 人以上' },
]

/* ── 菜品库预览（M0 用它证明前后端已打通）── */
const dishes = ref<DishBrief[]>([])
const loadingDishes = ref(false)

async function loadDishes() {
  loadingDishes.value = true
  try {
    const page = await dishApi.list({ pageSize: 6 })
    dishes.value = page.items
  } catch {
    dishes.value = []
  } finally {
    loadingDishes.value = false
  }
}

function toggleMood(tag: string) {
  const idx = selectedMoods.value.indexOf(tag)
  if (idx >= 0) {
    selectedMoods.value.splice(idx, 1)
  } else if (selectedMoods.value.length < 3) {
    selectedMoods.value.push(tag)
  } else {
    uni.showToast({ title: '最多选 3 个', icon: 'none' })
  }
}

function onBudgetMin(e: { detail: { value: number } }) {
  budgetMin.value = e.detail.value
  if (budgetMin.value > budgetMax.value) budgetMax.value = budgetMin.value
}

function onBudgetMax(e: { detail: { value: number } }) {
  budgetMax.value = e.detail.value
  if (budgetMax.value < budgetMin.value) budgetMin.value = budgetMax.value
}

async function onDecide() {
  if (deciding.value) return

  if (!meta.isOnline) {
    uni.showModal({
      title: '还没连上后端',
      content: `当前 API 地址：${meta.apiBaseUrl}\n\n请确认 src/FoodMate.Api 已启动。`,
      showCancel: false,
    })
    return
  }

  deciding.value = true

  try {
    const response = await decision.suggest({
      // 不传餐次，交给服务端按当前时间推断
      mealType: null,
      diningMode: diningMode.value,
      partySize: partySize.value,
      budgetMinCents: budgetMin.value,
      budgetMaxCents: budgetMax.value,
      moodTags: [...selectedMoods.value],
      exploreJitter: 0,
    })

    if (response.ranked.length === 0) {
      uni.showModal({
        title: '没找到合适的菜',
        content: '试试放宽预算，或检查口味画像里是否设置了忌口。',
        showCancel: false,
      })
      return
    }

    uni.navigateTo({ url: '/pages/decide/result' })
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    deciding.value = false
  }
}

function onDishTap(dish: DishBrief) {
  uni.showModal({
    title: dish.name,
    content:
      `${dish.cuisineLabel} · ${dish.categoryLabel} · ${dish.spicyLabel}\n` +
      `${formatPriceRange(dish.priceMinCents, dish.priceMaxCents)}` +
      (dish.calories ? ` · 约 ${dish.calories} kcal` : '') +
      `\n标签：${dish.tags.join('、') || '无'}` +
      (dish.hasRecipe ? `\n可自己做（约 ${dish.cookMinutes} 分钟）` : ''),
    showCancel: false,
  })
}

onShow(() => {
  void meta.checkConnection()
  void loadDishes()
})
</script>

<template>
  <view class="page">
    <!-- 问候 -->
    <view class="hero">
      <text class="hero__greeting">{{ greetingText }} 👋</text>
      <text class="hero__prompt">{{ promptText }}</text>
    </view>

    <!-- 怎么吃 -->
    <view class="fm-card">
      <text class="section-title">怎么吃</text>
      <view class="fm-chips">
        <view
          v-for="m in diningModes"
          :key="m.value"
          class="fm-chip"
          :class="{ 'fm-chip--active': diningMode === m.value }"
          @tap="diningMode = m.value"
        >
          {{ m.label }}
        </view>
      </view>
    </view>

    <!-- 预算 -->
    <view class="fm-card">
      <view class="section-head">
        <text class="section-title">预算</text>
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

    <!-- 人数 -->
    <view class="fm-card">
      <text class="section-title">几个人吃</text>
      <view class="fm-chips">
        <view
          v-for="p in partyOptions"
          :key="p.value"
          class="fm-chip"
          :class="{ 'fm-chip--active': partySize === p.value }"
          @tap="partySize = p.value"
        >
          {{ p.label }}
        </view>
      </view>
    </view>

    <!-- 想吃点 -->
    <view class="fm-card">
      <view class="section-head">
        <text class="section-title">想吃点</text>
        <text class="section-hint">最多 3 个</text>
      </view>
      <view class="fm-chips">
        <view
          v-for="tag in moodTags"
          :key="tag"
          class="fm-chip"
          :class="{ 'fm-chip--active': selectedMoods.includes(tag) }"
          @tap="toggleMood(tag)"
        >
          {{ tag }}
        </view>
      </view>
    </view>

    <!-- 主 CTA -->
    <view
      class="fm-button"
      :class="{ 'fm-button--disabled': deciding }"
      @tap="onDecide"
    >
      {{ deciding ? '正在挑…' : '帮我决定 🤔' }}
    </view>

    <!-- 后端连接状态 -->
    <view class="status" :class="`status--${meta.state}`">
      <text class="status__dot">●</text>
      <view class="status__body">
        <text class="status__title">
          {{ meta.isOnline ? '后端已连接' : meta.state === 'checking' ? '正在连接…' : '后端未连接' }}
        </text>
        <text class="status__detail">{{ meta.apiBaseUrl }}</text>
        <text v-if="meta.isOnline" class="status__detail">
          {{ meta.health?.name }} v{{ meta.health?.version }} · {{ meta.health?.environment }} ·
          菜品 {{ meta.dishCount }} 道 · {{ meta.platformLabel }}端
        </text>
        <text v-else-if="meta.errorMessage" class="status__detail status__detail--error">
          {{ meta.errorMessage }}
        </text>
      </view>
    </view>

    <!-- 菜品库预览 -->
    <view class="section-head section-head--list">
      <text class="section-title">菜品库</text>
      <text class="section-hint">{{ dishes.length }} / {{ meta.dishCount }} 道</text>
    </view>

    <view v-if="loadingDishes" class="empty">加载中…</view>
    <view v-else-if="dishes.length === 0" class="empty">
      暂无菜品。请确认后端已启动并完成种子数据导入。
    </view>
    <view v-else class="dish-list">
      <view
        v-for="dish in dishes"
        :key="dish.id"
        class="dish"
        @tap="onDishTap(dish)"
      >
        <view class="dish__head">
          <text class="dish__name">{{ dish.name }}</text>
          <text class="dish__price">{{ formatPriceRange(dish.priceMinCents, dish.priceMaxCents) }}</text>
        </view>
        <view class="dish__meta">
          <text class="dish__tag">{{ dish.cuisineLabel }}</text>
          <text class="dish__tag">{{ dish.categoryLabel }}</text>
          <text class="dish__tag">{{ dish.spicyLabel }}</text>
          <text v-if="dish.calories" class="dish__tag">{{ dish.calories }} kcal</text>
          <text v-if="dish.hasRecipe" class="dish__tag dish__tag--recipe">
            可自己做 {{ dish.cookMinutes }} 分钟
          </text>
        </view>
      </view>
    </view>

    <view class="footer">美食伴侣 · M0 骨架</view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 问候 ── */
.hero {
  padding: $fm-gap-md 8rpx $fm-gap-lg;
  display: flex;
  flex-direction: column;

  &__greeting {
    font-size: 30rpx;
    color: $fm-text-secondary;
  }

  &__prompt {
    margin-top: 8rpx;
    font-size: 44rpx;
    font-weight: 700;
    line-height: 1.35;
  }
}

/* ── 小节标题 ── */
.section-title {
  font-size: 30rpx;
  font-weight: 600;
  display: block;
  margin-bottom: $fm-gap-md;
}

.section-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;

  &--list {
    margin: $fm-gap-lg 8rpx $fm-gap-md;
  }

  .section-title {
    margin-bottom: $fm-gap-md;
  }
}

.section-value {
  font-size: 30rpx;
  font-weight: 600;
  color: $fm-primary;
}

.section-hint {
  font-size: 24rpx;
  color: $fm-text-tertiary;
}

/* ── 滑块 ── */
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

/* ── 连接状态 ── */
.status {
  display: flex;
  align-items: flex-start;
  gap: $fm-gap-sm;
  margin-top: $fm-gap-lg;
  padding: $fm-gap-md $fm-gap-lg;
  border-radius: $fm-radius-md;
  background: $fm-bg-card;

  &__dot {
    font-size: 22rpx;
    line-height: 1.8;
  }

  &__body {
    flex: 1;
    display: flex;
    flex-direction: column;
  }

  &__title {
    font-size: 28rpx;
    font-weight: 600;
  }

  &__detail {
    font-size: 22rpx;
    color: $fm-text-tertiary;
    word-break: break-all;

    &--error {
      color: $fm-danger;
    }
  }

  &--online &__dot {
    color: $fm-success;
  }

  &--offline &__dot {
    color: $fm-danger;
  }

  &--checking &__dot,
  &--unknown &__dot {
    color: $fm-warning;
  }
}

/* ── 菜品列表 ── */
.dish-list {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-sm;
}

.dish {
  background: $fm-bg-card;
  border-radius: $fm-radius-md;
  padding: $fm-gap-md $fm-gap-lg;

  &__head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
  }

  &__name {
    font-size: 30rpx;
    font-weight: 600;
  }

  &__price {
    font-size: 26rpx;
    color: $fm-primary;
    font-weight: 600;
  }

  &__meta {
    display: flex;
    flex-wrap: wrap;
    gap: 10rpx;
    margin-top: 12rpx;
  }

  &__tag {
    font-size: 22rpx;
    color: $fm-text-tertiary;
    background: $fm-bg-muted;
    border-radius: $fm-radius-sm;
    padding: 4rpx 14rpx;

    &--recipe {
      background: $fm-primary-soft;
      color: $fm-primary;
    }
  }
}

.empty {
  padding: 60rpx 0;
  text-align: center;
  font-size: 26rpx;
  color: $fm-text-tertiary;
}

.footer {
  margin-top: $fm-gap-lg;
  text-align: center;
  font-size: 22rpx;
  color: $fm-text-tertiary;
}
</style>
