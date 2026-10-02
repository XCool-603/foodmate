<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import DishWheel from '@/components/DishWheel.vue'
import { engineApi } from '@/api'
import type { EngineMeta, ScoredDish } from '@/api'
import { useDecisionStore, useRecordStore } from '@/stores'
import { formatPriceRange } from '@/utils/format'

const store = useDecisionStore()
const records = useRecordStore()

const wheelRef = ref<InstanceType<typeof DishWheel> | null>(null)
const engine = ref<EngineMeta | null>(null)
const spinning = ref(false)
const highlighted = ref<number>(-1)
const expandedDishId = ref<string | null>(null)
const choosing = ref(false)

const wheelItems = computed(() =>
  store.wheelItems.map((item) => ({ id: item.dishId, name: item.name })),
)

const summary = computed(() => {
  const data = store.result
  if (!data) return ''
  return `候选 ${data.candidateCount} 道 · 过滤 ${data.filteredOutCount} 道 · 耗时 ${data.elapsedMs}ms`
})

const relaxedHint = computed(() => {
  const level = store.result?.relaxedLevel ?? 0
  if (level === 0) return ''
  return '符合条件的菜不多，已为你放宽了部分条件'
})

/** 某个维度的加权贡献（0–1），用于画条形图。 */
function contribution(item: ScoredDish, dimension: string): number {
  const weight = engine.value?.weights[dimension] ?? 0
  return weight * (item.breakdown[dimension] ?? 0)
}

function dimensionLabel(dimension: string): string {
  return engine.value?.dimensionLabels[dimension] ?? dimension
}

/** 按贡献降序的维度列表。 */
function dimensions(item: ScoredDish): string[] {
  return Object.keys(item.breakdown).sort(
    (a, b) => contribution(item, b) - contribution(item, a),
  )
}

function onSpin() {
  if (spinning.value || wheelItems.value.length === 0) return
  highlighted.value = -1
  spinning.value = true
  wheelRef.value?.spinRandom()
}

function onSpinDone(index: number) {
  spinning.value = false
  highlighted.value = index

  const picked = store.wheelItems[index]
  if (picked) {
    store.choose(picked.dishId, 'wheel')
  }

  // 轻震动反馈，强化「停下了」的感觉
  try {
    uni.vibrateShort({ type: 'light' })
  } catch {
    // 不支持时忽略
  }
}

function toggleDetail(dishId: string) {
  expandedDishId.value = expandedDishId.value === dishId ? null : dishId
}

async function onEatThis(item: ScoredDish, source: 'wheel' | 'list' = 'list') {
  if (choosing.value) return
  choosing.value = true

  try {
    // 回传选择是权重调优的核心数据，即使用户没记录也要发
    await store.choose(item.dishId, source)

    const sessionId = store.result?.sessionId
    const eatenAt = new Date().toISOString()

    const result = await records.create({
      dishId: item.dishId,
      dishName: item.name,
      mealType: currentMealType(),
      diningMode: currentDiningMode(),
      eatenAt,
      servings: 1,
      source: 3,
      decisionSessionId: sessionId ?? null,
    })

    if (!result.ok) {
      uni.showModal({
        title: '含忌口食材',
        content: `${result.conflict}\n\n仍要记录这一顿吗？`,
        confirmText: '仍然记录',
        success: async (res) => {
          if (!res.confirm) return

          try {
            await records.create({
              dishId: item.dishId,
              dishName: item.name,
              mealType: currentMealType(),
              diningMode: currentDiningMode(),
              eatenAt,
              servings: 1,
              source: 3,
              decisionSessionId: sessionId ?? null,
              force: true,
            })
            await afterRecorded(item.name)
          } catch (err) {
            uni.showToast({ title: (err as Error).message, icon: 'none' })
          }
        },
      })
      return
    }

    await afterRecorded(item.name)
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    choosing.value = false
  }
}

async function afterRecorded(dishName: string) {
  await records.loadStats()

  uni.showToast({ title: `已记录「${dishName}」`, icon: 'none' })

  setTimeout(() => {
    uni.showModal({
      title: '记下了',
      content: '要顺手打个分吗？评分会让下次推荐更准。',
      confirmText: '去打分',
      cancelText: '待会儿',
      success: (res) => {
        if (res.confirm) uni.switchTab({ url: '/pages/records/index' })
      },
    })
  }, 700)
}

/** 当前餐次（与服务端的推断规则保持一致）。 */
function currentMealType(): number {
  const hour = new Date().getHours()
  if (hour >= 5 && hour < 10) return 1
  if (hour >= 10 && hour < 15) return 2
  if (hour >= 15 && hour < 21) return 3
  return 4
}

/** 本次决策使用的就餐方式；「随便」时按堂食记录。 */
function currentDiningMode(): number {
  const mode = store.lastRequest?.diningMode ?? 0
  return mode === 0 ? 2 : mode
}

async function onRefresh() {
  if (store.loading) return

  highlighted.value = -1
  expandedDishId.value = null

  try {
    await store.refresh()
    uni.showToast({ title: '换了一批', icon: 'none' })
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  }
}

function onShare() {
  const top = store.result?.ranked[0]
  if (!top) return

  uni.setClipboardData({
    data: `今天吃「${top.name}」！${top.reasons.join('，')} —— 来自美食伴侣`,
    success: () => uni.showToast({ title: '推荐语已复制', icon: 'none' }),
  })
}

onLoad(async () => {
  if (!store.hasResult) {
    uni.showToast({ title: '还没有推荐结果', icon: 'none' })
    setTimeout(() => uni.navigateBack(), 800)
    return
  }

  try {
    engine.value = await engineApi.meta()
  } catch {
    // 权重拿不到只影响条形图比例，不影响主流程
  }
})
</script>

<template>
  <view class="page">
    <!-- 转盘 -->
    <view class="wheel-box">
      <DishWheel
        ref="wheelRef"
        :items="wheelItems"
        @done="onSpinDone"
      />

      <view
        class="spin-button"
        :class="{ 'spin-button--disabled': spinning || wheelItems.length === 0 }"
        @tap="onSpin"
      >
        {{ spinning ? '转着呢…' : '开始转 🎯' }}
      </view>

      <text class="summary">{{ summary }}</text>
      <text v-if="relaxedHint" class="relaxed">{{ relaxedHint }}</text>
    </view>

    <!-- 转盘抽中的结果 -->
    <view v-if="highlighted >= 0 && store.wheelItems[highlighted]" class="picked">
      <text class="picked__label">转盘选中</text>
      <text class="picked__name">{{ store.wheelItems[highlighted].name }}</text>
      <text class="picked__reason">
        {{ store.wheelItems[highlighted].reasons.join(' · ') }}
      </text>
      <view class="picked__button" @tap="onEatThis(store.wheelItems[highlighted], 'wheel')">
        就吃这个
      </view>
    </view>

    <!-- 推荐榜单 -->
    <view class="section-head">
      <text class="section-title">为什么是这些</text>
      <text class="section-hint">按分数排序</text>
    </view>

    <view
      v-for="item in store.result?.ranked ?? []"
      :key="item.dishId"
      class="dish"
      :class="{ 'dish--highlight': highlighted >= 0 && store.wheelItems[highlighted]?.dishId === item.dishId }"
    >
      <view class="dish__head">
        <view class="dish__rank">{{ item.rank }}</view>
        <view class="dish__main">
          <text class="dish__name">{{ item.name }}</text>
          <text class="dish__meta">
            {{ item.dish.cuisineLabel }} · {{ item.dish.categoryLabel }} ·
            {{ item.dish.spicyLabel }} ·
            {{ formatPriceRange(item.dish.priceMinCents, item.dish.priceMaxCents) }}
          </text>
        </view>
        <text class="dish__score">{{ item.score }}</text>
      </view>

      <view class="reasons">
        <text v-for="(reason, i) in item.reasons" :key="i" class="reason">{{ reason }}</text>
      </view>

      <view v-if="item.penalties.length" class="penalties">
        <text v-for="p in item.penalties" :key="p.code" class="penalty">
          {{ p.description }}（×{{ p.multiplier }}）
        </text>
      </view>

      <view class="dish__actions">
        <text class="link" @tap="toggleDetail(item.dishId)">
          {{ expandedDishId === item.dishId ? '收起' : '为什么推荐' }}
        </text>
        <text class="link link--primary" @tap="onEatThis(item)">就吃这个</text>
      </view>

      <!-- 打分明细 -->
      <view v-if="expandedDishId === item.dishId" class="breakdown">
        <view
          v-for="dim in dimensions(item)"
          :key="dim"
          class="bar-row"
        >
          <text class="bar-row__label">{{ dimensionLabel(dim) }}</text>
          <view class="bar-row__track">
            <view
              class="bar-row__fill"
              :style="{ width: `${Math.round(contribution(item, dim) * 100 * 3)}%` }"
            />
          </view>
          <text class="bar-row__value">{{ Math.round(contribution(item, dim) * 100) }}</text>
        </view>
        <text class="breakdown__hint">
          条形长度 = 权重 × 该维度得分，代表它对总分的贡献
        </text>
      </view>
    </view>

    <view v-if="!(store.result?.ranked.length)" class="empty">
      <text class="empty__title">没有符合条件的菜品</text>
      <text class="empty__desc">试试放宽预算，或检查是否设置了忌口</text>
    </view>

    <!-- 底部操作 -->
    <view class="actions">
      <view class="fm-button fm-button--ghost" @tap="onRefresh">换一批</view>
      <view class="fm-button fm-button--ghost" @tap="onShare">复制推荐语</view>
    </view>

    <view class="footer">
      引擎 v{{ store.result?.engineVersion ?? '—' }} · 抖动
      {{ store.jitterUsed > 0 ? `σ=${store.jitterUsed}` : '关闭（结果可复现）' }}
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 转盘 ── */
.wheel-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: $fm-gap-lg 0 $fm-gap-md;
}

.spin-button {
  margin-top: $fm-gap-lg;
  padding: 22rpx 72rpx;
  border-radius: 999rpx;
  background: $fm-primary;
  color: #fff;
  font-size: 30rpx;
  font-weight: 600;

  &--disabled {
    background: #d9dbe0;
  }
}

.summary {
  margin-top: $fm-gap-md;
  font-size: 22rpx;
  color: $fm-text-tertiary;
}

.relaxed {
  margin-top: 6rpx;
  font-size: 22rpx;
  color: $fm-warning;
}

/* ── 转盘结果卡 ── */
.picked {
  display: flex;
  flex-direction: column;
  align-items: center;
  background: $fm-primary-soft;
  border-radius: $fm-radius-lg;
  padding: $fm-gap-lg;
  margin-bottom: $fm-gap-lg;

  &__label {
    font-size: 22rpx;
    color: $fm-primary;
  }

  &__name {
    margin-top: 8rpx;
    font-size: 44rpx;
    font-weight: 700;
    color: $fm-text;
  }

  &__reason {
    margin-top: 10rpx;
    font-size: 26rpx;
    color: $fm-text-secondary;
    text-align: center;
  }

  &__button {
    margin-top: $fm-gap-lg;
    padding: 20rpx 64rpx;
    border-radius: 999rpx;
    background: $fm-primary;
    color: #fff;
    font-size: 30rpx;
    font-weight: 600;
  }
}

/* ── 小节标题 ── */
.section-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin: 0 8rpx $fm-gap-md;
}

.section-title {
  font-size: 30rpx;
  font-weight: 600;
}

.section-hint {
  font-size: 22rpx;
  color: $fm-text-tertiary;
}

/* ── 推荐项 ── */
.dish {
  background: $fm-bg-card;
  border-radius: $fm-radius-md;
  padding: $fm-gap-md $fm-gap-lg;
  margin-bottom: $fm-gap-sm;
  border: 2rpx solid transparent;

  &--highlight {
    border-color: $fm-primary;
  }

  &__head {
    display: flex;
    align-items: center;
    gap: $fm-gap-sm;
  }

  &__rank {
    width: 44rpx;
    height: 44rpx;
    border-radius: 50%;
    background: $fm-bg-muted;
    color: $fm-text-secondary;
    font-size: 24rpx;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }

  &__main {
    flex: 1;
    display: flex;
    flex-direction: column;
  }

  &__name {
    font-size: 30rpx;
    font-weight: 600;
  }

  &__meta {
    margin-top: 4rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }

  &__score {
    font-size: 34rpx;
    font-weight: 700;
    color: $fm-primary;
  }

  &__actions {
    display: flex;
    justify-content: space-between;
    margin-top: $fm-gap-md;
  }
}

.reasons {
  display: flex;
  flex-direction: column;
  margin-top: 10rpx;
  gap: 4rpx;
}

.reason {
  font-size: 25rpx;
  color: $fm-text-secondary;
  line-height: 1.5;
}

.penalties {
  display: flex;
  flex-wrap: wrap;
  gap: 8rpx;
  margin-top: 10rpx;
}

.penalty {
  font-size: 21rpx;
  color: $fm-danger;
  background: rgba(229, 72, 77, 0.08);
  border-radius: $fm-radius-sm;
  padding: 4rpx 12rpx;
}

.link {
  font-size: 25rpx;
  color: $fm-text-tertiary;

  &--primary {
    color: $fm-primary;
    font-weight: 600;
  }
}

/* ── 打分明细 ── */
.breakdown {
  margin-top: $fm-gap-md;
  padding-top: $fm-gap-md;
  border-top: 2rpx solid $fm-border;
}

.bar-row {
  display: flex;
  align-items: center;
  gap: 12rpx;
  margin-bottom: 10rpx;

  &__label {
    width: 130rpx;
    font-size: 22rpx;
    color: $fm-text-secondary;
    flex-shrink: 0;
  }

  &__track {
    flex: 1;
    height: 14rpx;
    border-radius: 999rpx;
    background: $fm-bg-muted;
    overflow: hidden;
  }

  &__fill {
    height: 100%;
    border-radius: 999rpx;
    background: $fm-primary;
  }

  &__value {
    width: 50rpx;
    text-align: right;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }
}

.breakdown__hint {
  display: block;
  margin-top: 6rpx;
  font-size: 20rpx;
  color: $fm-text-tertiary;
}

/* ── 其它 ── */
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 80rpx 0;

  &__title {
    font-size: 30rpx;
    font-weight: 600;
  }

  &__desc {
    margin-top: 8rpx;
    font-size: 24rpx;
    color: $fm-text-tertiary;
  }
}

.actions {
  display: flex;
  gap: $fm-gap-md;
  margin-top: $fm-gap-lg;

  .fm-button {
    flex: 1;
    height: 84rpx;
    font-size: 28rpx;
  }
}

.footer {
  margin-top: $fm-gap-lg;
  text-align: center;
  font-size: 21rpx;
  color: $fm-text-tertiary;
}
</style>
