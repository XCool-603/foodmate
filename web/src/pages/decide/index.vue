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

/* ── 决策条件（提交给决策引擎）── */
const diningMode = ref(0)
const budgetMin = ref(1500)
const budgetMax = ref(5000)
const partySize = ref(1)
const selectedMoods = ref<string[]>([])

const greetingText = computed(() => greeting())
const promptText = computed(() => mealPrompt())
const budgetText = computed(() => formatPriceRange(budgetMin.value, budgetMax.value))

/** 顶部状态栏的读数，模仿终端 HUD。 */
const systemLine = computed(() => {
  if (meta.state === 'online') {
    return `SYS · ONLINE · DISH ${meta.dishCount}`
  }
  if (meta.state === 'checking') {
    return 'SYS · CONNECTING…'
  }
  return 'SYS · OFFLINE'
})

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

/* ── 菜品库预览 ── */
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
  uni.navigateTo({ url: `/pages/dish/detail?id=${dish.id}` })
}

onShow(() => {
  void meta.checkConnection()
  void loadDishes()
})
</script>

<template>
  <view class="page">
    <!-- ── 顶部状态栏 ─────────────────────────────────── -->
    <view class="sysbar">
      <text class="sysbar__dot" :class="`sysbar__dot--${meta.state}`">●</text>
      <text class="sysbar__text">{{ systemLine }}</text>
    </view>

    <!-- ── 主视觉 ─────────────────────────────────────── -->
    <view class="hero">
      <text class="hero__greeting">{{ greetingText }}，欢迎回来</text>
      <text class="hero__title">{{ promptText }}</text>
      <view class="hero__scan" />
    </view>

    <!-- ── 怎么吃 ─────────────────────────────────────── -->
    <view class="fm-card">
      <view class="head">
        <text class="head__mark">01</text>
        <text class="head__title">怎么吃</text>
      </view>
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

    <!-- ── 预算 ───────────────────────────────────────── -->
    <view class="fm-card">
      <view class="head">
        <text class="head__mark">02</text>
        <text class="head__title">预算</text>
        <text class="head__value">{{ budgetText }}</text>
      </view>
      <view class="slider-row">
        <text class="slider-row__label">MIN</text>
        <slider
          class="slider-row__slider"
          :value="budgetMin"
          :min="0"
          :max="20000"
          :step="500"
          activeColor="#00F0FF"
          backgroundColor="#1B1B29"
          block-color="#00F0FF"
          block-size="18"
          @change="onBudgetMin"
        />
      </view>
      <view class="slider-row">
        <text class="slider-row__label">MAX</text>
        <slider
          class="slider-row__slider"
          :value="budgetMax"
          :min="0"
          :max="20000"
          :step="500"
          activeColor="#00F0FF"
          backgroundColor="#1B1B29"
          block-color="#00F0FF"
          block-size="18"
          @change="onBudgetMax"
        />
      </view>
    </view>

    <!-- ── 人数 ───────────────────────────────────────── -->
    <view class="fm-card">
      <view class="head">
        <text class="head__mark">03</text>
        <text class="head__title">几个人吃</text>
      </view>
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

    <!-- ── 想吃点 ─────────────────────────────────────── -->
    <view class="fm-card">
      <view class="head">
        <text class="head__mark">04</text>
        <text class="head__title">想吃点</text>
        <text class="head__hint">{{ selectedMoods.length }}/3</text>
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

    <!-- ── 主 CTA ─────────────────────────────────────── -->
    <view
      class="fm-button cta"
      :class="{ 'fm-button--disabled': deciding }"
      @tap="onDecide"
    >
      {{ deciding ? '正在计算…' : '帮我决定' }}
    </view>

    <!-- ── 连接状态 ───────────────────────────────────── -->
    <view class="status" :class="`status--${meta.state}`">
      <view class="status__row">
        <text class="status__key">ENDPOINT</text>
        <text class="status__val">{{ meta.apiBaseUrl }}</text>
      </view>
      <view v-if="meta.isOnline" class="status__row">
        <text class="status__key">SERVICE</text>
        <text class="status__val status__val--ok">
          v{{ meta.health?.version }} · {{ meta.health?.environment }} · {{ meta.platformLabel }}
        </text>
      </view>
      <view v-else-if="meta.errorMessage" class="status__row">
        <text class="status__key">ERROR</text>
        <text class="status__val status__val--err">{{ meta.errorMessage }}</text>
      </view>
    </view>

    <!-- ── 菜品库 ─────────────────────────────────────── -->
    <view class="head head--list">
      <text class="head__mark">DB</text>
      <text class="head__title">菜品库</text>
      <text class="head__hint">{{ dishes.length }} / {{ meta.dishCount }}</text>
    </view>

    <view v-if="loadingDishes" class="empty">读取中…</view>
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
            可自制 {{ dish.cookMinutes }}min
          </text>
        </view>
      </view>
    </view>

    <view class="footer">FOODMATE · M5 · NEON BUILD</view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 顶部状态栏 ───────────────────────────────────────────── */
.sysbar {
  display: flex;
  align-items: center;
  gap: 10rpx;
  padding: 0 4rpx 8rpx;

  &__dot {
    font-size: 18rpx;

    &--online {
      color: $cy-lime;
      text-shadow: 0 0 10rpx rgba(0, 255, 159, 0.8);
    }

    &--offline {
      color: $cy-red;
      text-shadow: 0 0 10rpx rgba(255, 59, 92, 0.8);
    }

    &--checking,
    &--unknown {
      color: $cy-amber;
      animation: fm-pulse 1.2s ease-in-out infinite;
    }
  }

  &__text {
    @include hud-label($cy-text-faint);
  }
}

/* ── 主视觉 ───────────────────────────────────────────────── */
.hero {
  position: relative;
  padding: $fm-gap-md 4rpx $fm-gap-lg;
  margin-bottom: $fm-gap-md;
  overflow: hidden;

  &__greeting {
    display: block;
    @include hud-label($cy-cyan);
    opacity: 0.75;
  }

  &__title {
    display: block;
    margin-top: 14rpx;
    font-size: 52rpx;
    font-weight: 800;
    line-height: 1.3;
    letter-spacing: 1rpx;
    color: $cy-text;
    text-shadow:
      0 0 18rpx rgba(0, 240, 255, 0.35),
      2rpx 0 0 rgba(255, 46, 151, 0.35),
      -2rpx 0 0 rgba(0, 240, 255, 0.35);
    animation: fm-glitch 6s steps(1) infinite;
  }

  /* 扫描线覆盖，只有这一块有 */
  &__scan {
    position: absolute;
    inset: 0;
    @include scanlines(0.035);
    pointer-events: none;
  }
}

/* ── 区块标题 ─────────────────────────────────────────────── */
.head {
  display: flex;
  align-items: baseline;
  gap: 14rpx;
  margin-bottom: $fm-gap-md;

  &--list {
    margin: $fm-gap-lg 8rpx $fm-gap-md;
  }

  &__mark {
    font-family: $cy-mono;
    font-size: 20rpx;
    font-weight: 700;
    color: $cy-void;
    background: $cy-cyan;
    padding: 2rpx 10rpx;
    box-shadow: 0 0 12rpx rgba(0, 240, 255, 0.5);
  }

  &__title {
    font-size: 30rpx;
    font-weight: 700;
    letter-spacing: 2rpx;
  }

  &__value {
    margin-left: auto;
    @include neon-text($cy-cyan);
    font-family: $cy-mono;
    font-size: 28rpx;
    font-weight: 700;
  }

  &__hint {
    margin-left: auto;
    @include hud-label();
  }
}

/* ── 滑块 ─────────────────────────────────────────────────── */
.slider-row {
  display: flex;
  align-items: center;

  & + & {
    margin-top: 8rpx;
  }

  &__label {
    width: 70rpx;
    @include hud-label();
  }

  &__slider {
    flex: 1;
    margin: 0;
  }
}

/* ── CTA ──────────────────────────────────────────────────── */
.cta {
  margin-top: $fm-gap-lg;
  height: 108rpx;
  font-size: 34rpx;
}

/* ── 连接状态 ─────────────────────────────────────────────── */
.status {
  margin-top: $fm-gap-lg;
  padding: $fm-gap-md $fm-gap-lg;
  background: $cy-surface;
  border: 1px solid $cy-line;
  border-left: 3px solid $cy-cyan;

  &--offline {
    border-left-color: $cy-red;
  }

  &--checking,
  &--unknown {
    border-left-color: $cy-amber;
  }

  &__row {
    display: flex;
    align-items: flex-start;
    gap: $fm-gap-md;

    & + & {
      margin-top: 8rpx;
    }
  }

  &__key {
    width: 130rpx;
    flex-shrink: 0;
    @include hud-label();
  }

  &__val {
    flex: 1;
    font-family: $cy-mono;
    font-size: 22rpx;
    color: $cy-text-dim;
    word-break: break-all;

    &--ok {
      color: $cy-lime;
    }

    &--err {
      color: $cy-red;
    }
  }
}

/* ── 菜品列表 ─────────────────────────────────────────────── */
.dish-list {
  display: flex;
  flex-direction: column;
  gap: 2rpx;
}

.dish {
  position: relative;
  padding: $fm-gap-md $fm-gap-lg;
  background: $cy-surface;
  border: 1px solid $cy-line;
  transition: all 0.16s ease;

  /* 左侧霓虹条：hover/点击时的"通电"感 */
  &::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: 2px;
    background: rgba(0, 240, 255, 0.28);
  }

  &__head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
  }

  &__name {
    font-size: 30rpx;
    font-weight: 700;
    letter-spacing: 1rpx;
  }

  &__price {
    @include neon-text($cy-amber);
    font-family: $cy-mono;
    font-size: 26rpx;
    font-weight: 700;
  }

  &__meta {
    display: flex;
    flex-wrap: wrap;
    gap: 10rpx;
    margin-top: 12rpx;
  }

  &__tag {
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
    border: 1px solid $cy-line;
    padding: 2rpx 12rpx;

    &--recipe {
      color: $cy-cyan;
      border-color: rgba(0, 240, 255, 0.4);
      background: rgba(0, 240, 255, 0.06);
    }
  }
}

.empty {
  padding: 60rpx 0;
  text-align: center;
  font-family: $cy-mono;
  font-size: 24rpx;
  color: $cy-text-faint;
}

.footer {
  margin-top: $fm-gap-lg;
  text-align: center;
  @include hud-label($cy-text-faint);
  opacity: 0.6;
}
</style>
