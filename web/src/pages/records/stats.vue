<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import AnimatedNumber from '@/components/AnimatedNumber.vue'
import SkeletonBlock from '@/components/SkeletonBlock.vue'
import { recordApi } from '@/api'
import type { DistributionItem, RecordStats } from '@/api'

/**
 * 数据看板。
 *
 * 图表全部用 CSS 画，不用 canvas ——
 * 柱状图与分布条用 flex + 百分比高度就能表达清楚，
 * 而且在小程序端的表现比 canvas 稳定得多（不用处理尺寸与重绘时机）。
 */
const stats = ref<RecordStats | null>(null)
const loading = ref(true)
const range = ref<'week' | 'month' | 'all'>('week')

const RANGES = [
  { key: 'week' as const, label: '本周', days: 7 },
  { key: 'month' as const, label: '本月', days: 30 },
  { key: 'all' as const, label: '全部', days: 0 },
]

const activeRange = computed(() => RANGES.find((r) => r.key === range.value) ?? RANGES[0])

/** 逐日热量的最大值，用于算柱高。 */
const maxCalories = computed(() => {
  const list = stats.value?.dailyCalories ?? []
  return Math.max(1, ...list.map((d) => d.calories))
})

/** 分布数据里最大的 count，用于算条长。 */
function maxCount(list: DistributionItem[]): number {
  return Math.max(1, ...list.map((i) => i.count))
}

function barWidth(list: DistributionItem[], item: DistributionItem): number {
  return Math.round((item.count / maxCount(list)) * 100)
}

/** 柱状图的日期标签：只显示「日」。 */
function dayLabel(date: string): string {
  const parts = date.split('-')
  return parts.length >= 3 ? String(Number(parts[2])) : date
}

async function load() {
  loading.value = true

  try {
    if (range.value === 'all') {
      stats.value = await recordApi.stats()
    } else {
      const to = new Date()
      const from = new Date()
      from.setDate(from.getDate() - (activeRange.value.days - 1))

      const fmt = (d: Date) => {
        const pad = (n: number) => String(n).padStart(2, '0')
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
      }

      stats.value = await recordApi.stats(fmt(from), fmt(to))
    }
  } catch {
    stats.value = null
  } finally {
    loading.value = false
  }
}

function onRange(key: 'week' | 'month' | 'all') {
  if (range.value === key) return
  range.value = key
  void load()
}

onShow(() => {
  void load()
})
</script>

<template>
  <view class="page">
    <!-- ── 区间切换 ───────────────────────────────────── -->
    <view class="ranges">
      <view
        v-for="r in RANGES"
        :key="r.key"
        class="ranges__item"
        :class="{ 'ranges__item--active': range === r.key }"
        hover-class="hover-dim"
        @tap="onRange(r.key)"
      >
        {{ r.label }}
      </view>
    </view>

    <SkeletonBlock v-if="loading" :rows="2" hero />

    <template v-else-if="stats">
      <!-- ── 核心指标 ─────────────────────────────────── -->
      <view class="metrics anim-stagger">
        <view class="metric">
          <AnimatedNumber class="metric__value" :value="stats.totalRecords" />
          <text class="metric__label">记录餐数</text>
        </view>
        <view class="metric">
          <AnimatedNumber class="metric__value" :value="stats.recordedDays" />
          <text class="metric__label">打卡天数</text>
        </view>
        <view class="metric">
          <AnimatedNumber class="metric__value" :value="stats.newDishesTried" />
          <text class="metric__label">尝新菜品</text>
        </view>
      </view>

      <!-- ── 热量与评分 ───────────────────────────────── -->
      <view class="fm-card">
        <view class="head">
          <text class="head__mark">SUM</text>
          <text class="head__title">摄入概览</text>
        </view>

        <view class="summary">
          <view class="summary__item">
            <AnimatedNumber class="summary__value" :value="stats.totalCalories" />
            <text class="summary__unit">kcal 总摄入</text>
          </view>
          <view class="summary__divider" />
          <view class="summary__item">
            <AnimatedNumber class="summary__value" :value="stats.averageCaloriesPerMeal" />
            <text class="summary__unit">kcal 每餐均值</text>
          </view>
          <view class="summary__divider" />
          <view class="summary__item">
            <AnimatedNumber
              class="summary__value"
              :value="stats.averageRating ?? 0"
              :decimals="1"
            />
            <text class="summary__unit">平均评分</text>
          </view>
        </view>

        <text class="note">只做呈现，不做评判 —— 吃多吃少都是你的选择</text>
      </view>

      <!-- ── 热量趋势 ─────────────────────────────────── -->
      <view class="fm-card">
        <view class="head">
          <text class="head__mark">DAY</text>
          <text class="head__title">逐日热量</text>
          <text class="head__hint">{{ stats.dailyCalories.length }} 天</text>
        </view>

        <view v-if="!stats.dailyCalories.length" class="empty">这个区间还没有记录</view>

        <view v-else class="chart">
          <view
            v-for="(d, i) in stats.dailyCalories"
            :key="d.date"
            class="chart__col"
            :style="{ animationDelay: `${i * 0.035}s` }"
          >
            <text class="chart__value">{{ d.calories }}</text>
            <view class="chart__track">
              <view
                class="chart__bar"
                :style="{ height: `${Math.max(4, Math.round((d.calories / maxCalories) * 100))}%` }"
              />
            </view>
            <text class="chart__label">{{ dayLabel(d.date) }}</text>
          </view>
        </view>
      </view>

      <!-- ── 菜系分布 ─────────────────────────────────── -->
      <view class="fm-card">
        <view class="head">
          <text class="head__mark">CU</text>
          <text class="head__title">菜系分布</text>
        </view>

        <view v-if="!stats.cuisineDistribution.length" class="empty">暂无数据</view>

        <view v-else class="bars anim-stagger">
          <view v-for="item in stats.cuisineDistribution" :key="item.value" class="bar">
            <text class="bar__label">{{ item.label }}</text>
            <view class="bar__track">
              <view
                class="bar__fill"
                :style="{ width: `${barWidth(stats.cuisineDistribution, item)}%` }"
              />
            </view>
            <text class="bar__value">{{ item.count }}</text>
          </view>
        </view>
      </view>

      <!-- ── 餐次分布 ─────────────────────────────────── -->
      <view class="fm-card">
        <view class="head">
          <text class="head__mark">MT</text>
          <text class="head__title">餐次分布</text>
        </view>

        <view v-if="!stats.mealTypeDistribution.length" class="empty">暂无数据</view>

        <view v-else class="bars anim-stagger">
          <view v-for="item in stats.mealTypeDistribution" :key="item.value" class="bar">
            <text class="bar__label">{{ item.label }}</text>
            <view class="bar__track">
              <view
                class="bar__fill bar__fill--magenta"
                :style="{ width: `${barWidth(stats.mealTypeDistribution, item)}%` }"
              />
            </view>
            <text class="bar__value">{{ item.count }}</text>
          </view>
        </view>
      </view>

      <!-- ── 就餐方式 ─────────────────────────────────── -->
      <view class="fm-card">
        <view class="head">
          <text class="head__mark">DM</text>
          <text class="head__title">就餐方式</text>
        </view>

        <view v-if="!stats.diningModeDistribution.length" class="empty">暂无数据</view>

        <view v-else class="bars anim-stagger">
          <view v-for="item in stats.diningModeDistribution" :key="item.value" class="bar">
            <text class="bar__label">{{ item.label }}</text>
            <view class="bar__track">
              <view
                class="bar__fill bar__fill--amber"
                :style="{ width: `${barWidth(stats.diningModeDistribution, item)}%` }"
              />
            </view>
            <text class="bar__value">{{ item.count }}</text>
          </view>
        </view>
      </view>
    </template>

    <view v-else class="empty">加载失败，请检查后端连接</view>

    <view class="footer">
      {{ stats?.from }} — {{ stats?.to }}
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 区间切换 ─────────────────────────────────────────────── */
.ranges {
  display: flex;
  gap: $fm-gap-sm;
  margin-bottom: $fm-gap-md;
  padding: 6rpx;
  background: $cy-surface;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;

  &__item {
    flex: 1;
    text-align: center;
    padding: 16rpx 0;
    font-family: $cy-mono;
    font-size: 25rpx;
    letter-spacing: 2rpx;
    color: $cy-text-faint;
    border-radius: $fm-radius-sm;
    transition: all 0.24s ease;

    &--active {
      color: $cy-cyan;
      background: rgba(0, 240, 255, 0.1);
      font-weight: 700;
      text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.6);
    }
  }
}

/* ── 指标卡 ───────────────────────────────────────────────── */
.metrics {
  display: flex;
  gap: $fm-gap-sm;
  margin-bottom: $fm-gap-md;
}

.metric {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6rpx;
  padding: $fm-gap-lg 0;
  background:
    linear-gradient(150deg, rgba(0, 240, 255, 0.06), transparent 55%),
    $cy-surface;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;

  &__value {
    font-size: 52rpx;
    font-weight: 800;
    color: $cy-cyan;
    text-shadow: 0 0 18rpx rgba(0, 240, 255, 0.55);
  }

  &__label {
    font-family: $cy-mono;
    font-size: 20rpx;
    letter-spacing: 1rpx;
    color: $cy-text-faint;
  }
}

/* ── 小节标题 ─────────────────────────────────────────────── */
.head {
  display: flex;
  align-items: baseline;
  gap: 14rpx;
  margin-bottom: $fm-gap-lg;

  &__mark {
    font-family: $cy-mono;
    font-size: 19rpx;
    font-weight: 700;
    color: $cy-void;
    background: $cy-cyan;
    padding: 2rpx 10rpx;
    border-radius: $fm-radius-sm;
    box-shadow: 0 0 12rpx rgba(0, 240, 255, 0.5);
  }

  &__title {
    font-size: 30rpx;
    font-weight: 700;
    letter-spacing: 2rpx;
  }

  &__hint {
    margin-left: auto;
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
  }
}

/* ── 概览 ─────────────────────────────────────────────────── */
.summary {
  display: flex;
  align-items: center;

  &__item {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 4rpx;
  }

  &__value {
    font-size: 40rpx;
    font-weight: 800;
    color: $cy-cyan;
    text-shadow: 0 0 14rpx rgba(0, 240, 255, 0.5);
  }

  &__unit {
    font-family: $cy-mono;
    font-size: 19rpx;
    color: $cy-text-faint;
  }

  &__divider {
    width: 1px;
    height: 60rpx;
    background: $cy-line;
  }
}

.note {
  display: block;
  margin-top: $fm-gap-lg;
  padding-top: $fm-gap-md;
  border-top: 1px solid $cy-line;
  font-size: 22rpx;
  color: $cy-text-faint;
  text-align: center;
}

/* ── 柱状图 ───────────────────────────────────────────────── */
.chart {
  display: flex;
  align-items: flex-end;
  gap: 8rpx;
  height: 300rpx;

  &__col {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    height: 100%;
    /* 柱子从下往上长出来 */
    animation: bar-rise 0.6s cubic-bezier(0.22, 1, 0.36, 1) both;
  }

  &__value {
    font-family: $cy-mono;
    font-size: 17rpx;
    color: $cy-text-faint;
    margin-bottom: 6rpx;
  }

  &__track {
    flex: 1;
    width: 100%;
    display: flex;
    align-items: flex-end;
  }

  &__bar {
    width: 100%;
    border-radius: $fm-radius-sm $fm-radius-sm 0 0;
    background: linear-gradient(180deg, $cy-cyan, rgba(255, 46, 151, 0.75));
    box-shadow: 0 0 12rpx rgba(0, 240, 255, 0.5);
    transition: height 0.5s cubic-bezier(0.22, 1, 0.36, 1);
  }

  &__label {
    margin-top: 8rpx;
    font-family: $cy-mono;
    font-size: 18rpx;
    color: $cy-text-faint;
  }
}

@keyframes bar-rise {
  from {
    opacity: 0;
    transform: translateY(20rpx);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* ── 分布条 ───────────────────────────────────────────────── */
.bars {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-md;
}

.bar {
  display: flex;
  align-items: center;
  gap: $fm-gap-md;

  &__label {
    width: 130rpx;
    flex-shrink: 0;
    font-size: 24rpx;
    color: $cy-text-dim;
  }

  &__track {
    flex: 1;
    height: 16rpx;
    border-radius: $fm-radius-pill;
    background: $cy-surface-2;
    border: 1px solid $cy-line;
    overflow: hidden;
  }

  &__fill {
    height: 100%;
    border-radius: $fm-radius-pill;
    background: linear-gradient(90deg, $cy-cyan, rgba(0, 240, 255, 0.4));
    box-shadow: 0 0 10rpx rgba(0, 240, 255, 0.5);
    transform-origin: left center;
    /* 条从左侧长出来。
       用 scaleX 而不是动画 width —— width 是内联样式，关键帧覆盖不了它 */
    animation: bar-grow 0.7s cubic-bezier(0.22, 1, 0.36, 1) both;

    &--magenta {
      background: linear-gradient(90deg, $cy-magenta, rgba(255, 46, 151, 0.4));
      box-shadow: 0 0 10rpx rgba(255, 46, 151, 0.5);
    }

    &--amber {
      background: linear-gradient(90deg, $cy-amber, rgba(255, 197, 61, 0.4));
      box-shadow: 0 0 10rpx rgba(255, 197, 61, 0.5);
    }
  }

  &__value {
    width: 60rpx;
    text-align: right;
    font-family: $cy-mono;
    font-size: 23rpx;
    font-weight: 700;
    color: $cy-cyan;
  }
}

@keyframes bar-grow {
  from {
    transform: scaleX(0);
    opacity: 0.4;
  }
  to {
    transform: scaleX(1);
    opacity: 1;
  }
}

/* ── 其它 ─────────────────────────────────────────────────── */
.empty {
  padding: 50rpx 0;
  text-align: center;
  font-family: $cy-mono;
  font-size: 23rpx;
  color: $cy-text-faint;
}

.footer {
  margin-top: $fm-gap-lg;
  text-align: center;
  @include hud-label($cy-text-faint);
  opacity: 0.7;
}
</style>
