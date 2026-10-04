<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import GachaCard from '@/components/GachaCard.vue'
import AnimatedNumber from '@/components/AnimatedNumber.vue'
import { engineApi } from '@/api'
import type { EngineMeta, ScoredDish } from '@/api'
import { useDecisionStore, useRecordStore } from '@/stores'
import { formatPriceRange } from '@/utils/format'

const store = useDecisionStore()
const records = useRecordStore()

const engine = ref<EngineMeta | null>(null)

/** 当前抽中的卡 */
const drawn = ref<ScoredDish | null>(null)
/** 悬念中（牌堆抖动） */
const drawing = ref(false)
/** 卡面已翻开 */
const revealed = ref(false)
/** 已抽过的菜，避免重复 */
const drawnIds = ref<Set<string>>(new Set())
/** 记录成功后的庆祝反馈 */
const celebrating = ref(false)

const highlighted = ref<string | null>(null)
const expandedDishId = ref<string | null>(null)
const choosing = ref(false)

/** 可抽的候选：优先给没抽过的 */
const drawPool = computed(() => {
  const all = store.wheelItems
  const fresh = all.filter((i) => !drawnIds.value.has(i.dishId))
  return fresh.length > 0 ? fresh : all
})

const summary = computed(() => {
  const data = store.result
  if (!data) return ''
  return `候选 ${data.candidateCount} · 过滤 ${data.filteredOutCount} · ${data.elapsedMs}ms`
})

const relaxedHint = computed(() =>
  (store.result?.relaxedLevel ?? 0) > 0 ? '符合条件的菜不多，已放宽部分条件' : '',
)

const drawLabel = computed(() => {
  if (drawing.value) return '抽取中…'
  if (!drawn.value) return '抽一张'
  return drawnIds.value.size >= store.wheelItems.length ? '重新抽' : '再抽一次'
})

/** 抽卡 */
function onDraw() {
  if (drawing.value || store.wheelItems.length === 0) return

  drawing.value = true
  revealed.value = false
  drawn.value = null
  highlighted.value = null

  // 悬念：牌堆抖动一下再出牌，比瞬间出结果更有"抽"的感觉
  setTimeout(() => {
    const pool = drawPool.value
    const pick = pool[Math.floor(Math.random() * pool.length)]

    drawn.value = pick
    drawnIds.value = new Set([...drawnIds.value, pick.dishId])
    highlighted.value = pick.dishId
    drawing.value = false

    // 先让卡面出现（缩在牌堆里），下一帧再翻开，触发过渡动画
    setTimeout(() => {
      revealed.value = true
    }, 50)

    // 回传选择，这是权重调优的核心数据
    void store.choose(pick.dishId, 'card')

    try {
      uni.vibrateShort({ type: 'medium' })
    } catch {
      // 不支持时忽略
    }
  }, 420)
}

/** 抽完了，重置已抽记录 */
function onResetDeck() {
  drawnIds.value = new Set()
  drawn.value = null
  revealed.value = false
  highlighted.value = null
  uni.showToast({ title: '牌堆已重置', icon: 'none' })
}

function toggleDetail(dishId: string) {
  expandedDishId.value = expandedDishId.value === dishId ? null : dishId
}

function onOpenDish(dishId: string) {
  uni.navigateTo({ url: `/pages/dish/detail?id=${dishId}` })
}

async function onEatThis(item: ScoredDish) {
  if (choosing.value) return
  choosing.value = true

  try {
    await store.choose(item.dishId, 'list')

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

  // 先给一个明确的"完成了"的视觉反馈，再问要不要打分
  celebrating.value = true
  try {
    uni.vibrateShort({ type: 'heavy' })
  } catch {
    // 不支持时忽略
  }

  setTimeout(() => {
    celebrating.value = false
  }, 1100)

  setTimeout(() => {
    uni.showModal({
      title: '记下了',
      content: `「${dishName}」已加入记录。要顺手打个分吗？评分会让下次推荐更准。`,
      confirmText: '去打分',
      cancelText: '待会儿',
      success: (res) => {
        if (res.confirm) uni.switchTab({ url: '/pages/records/index' })
      },
    })
  }, 1250)
}

function currentMealType(): number {
  const hour = new Date().getHours()
  if (hour >= 5 && hour < 10) return 1
  if (hour >= 10 && hour < 15) return 2
  if (hour >= 15 && hour < 21) return 3
  return 4
}

function currentDiningMode(): number {
  const mode = store.lastRequest?.diningMode ?? 0
  return mode === 0 ? 2 : mode
}

async function onRefresh() {
  if (store.loading) return

  highlighted.value = null
  expandedDishId.value = null
  drawn.value = null
  revealed.value = false
  drawnIds.value = new Set()

  try {
    await store.refresh()
    uni.showToast({ title: '换了一批', icon: 'none' })
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  }
}

function contribution(item: ScoredDish, dimension: string): number {
  const weight = engine.value?.weights[dimension] ?? 0
  return weight * (item.breakdown[dimension] ?? 0)
}

function dimensionLabel(dimension: string): string {
  return engine.value?.dimensionLabels[dimension] ?? dimension
}

function dimensions(item: ScoredDish): string[] {
  return Object.keys(item.breakdown).sort(
    (a, b) => contribution(item, b) - contribution(item, a),
  )
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
    <!-- ── 记录成功的庆祝反馈 ─────────────────────────── -->
    <view v-if="celebrating" class="celebrate">
      <view class="celebrate__ring" />
      <view class="celebrate__ring celebrate__ring--2" />
      <view class="celebrate__core anim-celebrate">
        <text class="celebrate__glyph">✓</text>
      </view>
    </view>

    <!-- ── 抽卡区 ─────────────────────────────────────── -->
    <view class="gacha">
      <view class="gacha__head">
        <text class="gacha__title">✦ 今日抽卡</text>
        <text class="gacha__sub">
          从 {{ store.wheelItems.length }} 张候选里抽一张
        </text>
      </view>

      <GachaCard
        :item="drawn"
        :revealed="revealed"
        :drawing="drawing"
        @tap="drawn ? null : onDraw()"
      />

      <!-- 稀有度图例 -->
      <view class="legend">
        <view class="legend__item legend__item--ssr">
          <text class="legend__key">SSR</text>
          <text class="legend__desc">第 1 名</text>
        </view>
        <view class="legend__item legend__item--sr">
          <text class="legend__key">SR</text>
          <text class="legend__desc">前 3 名</text>
        </view>
        <view class="legend__item legend__item--r">
          <text class="legend__key">R</text>
          <text class="legend__desc">前 8 名</text>
        </view>
        <view class="legend__item legend__item--n">
          <text class="legend__key">N</text>
          <text class="legend__desc">其他</text>
        </view>
      </view>

      <!-- 操作 -->
      <view class="gacha__actions">
        <view
          class="fm-button gacha__draw"
          :class="{ 'fm-button--disabled': drawing }"
          @tap="onDraw"
        >
          {{ drawLabel }}
        </view>
        <view
          v-if="drawn"
          class="fm-button fm-button--ghost gacha__eat"
          :class="{ 'fm-button--disabled': choosing }"
          @tap="onEatThis(drawn)"
        >
          就吃这个
        </view>
      </view>

      <view v-if="drawn" class="gacha__extra">
        <text class="link" @tap="onOpenDish(drawn.dishId)">看做法</text>
        <text class="gacha__dot">·</text>
        <text class="link" @tap="onResetDeck">重置牌堆</text>
      </view>

      <text class="gacha__summary">{{ summary }}</text>
      <text v-if="relaxedHint" class="gacha__relaxed">{{ relaxedHint }}</text>
    </view>

    <!-- ── 完整榜单 ───────────────────────────────────── -->
    <view class="section-head">
      <text class="section-title">
        <text class="section-title__mark">▸</text>完整榜单
      </text>
      <text class="section-hint">RANKED BY SCORE</text>
    </view>

    <view
      v-for="item in store.result?.ranked ?? []"
      :key="item.dishId"
      class="dish"
      :class="{ 'dish--highlight': highlighted === item.dishId }"
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
        <AnimatedNumber class="dish__score" :value="item.score" :decimals="1" :duration="700" />
      </view>

      <view class="reasons">
        <view v-for="(reason, i) in item.reasons" :key="i" class="reason">
          <text class="reason__mark">›</text>
          <text class="reason__text">{{ reason }}</text>
        </view>
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
        <text class="link" @tap="onOpenDish(item.dishId)">看做法</text>
        <text class="link link--primary" @tap="onEatThis(item)">就吃这个</text>
      </view>

      <!-- 打分明细 -->
      <view v-if="expandedDishId === item.dishId" class="breakdown">
        <view v-for="dim in dimensions(item)" :key="dim" class="bar-row">
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
    </view>

    <view class="footer">
      引擎 v{{ store.result?.engineVersion ?? '—' }} · 稀有度按推荐排名划分
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 庆祝反馈 ─────────────────────────────────────────────── */
.celebrate {
  position: fixed;
  inset: 0;
  z-index: 999;
  display: flex;
  align-items: center;
  justify-content: center;
  pointer-events: none;
  background: radial-gradient(
    circle at 50% 50%,
    rgba(0, 240, 255, 0.14) 0%,
    transparent 55%
  );
  animation: celebrate-fade 1.1s ease-out both;

  &__ring {
    position: absolute;
    width: 200rpx;
    height: 200rpx;
    border-radius: 50%;
    border: 2px solid $cy-cyan;
    box-shadow: 0 0 30rpx rgba(0, 240, 255, 0.7);
    animation: celebrate-ring 0.9s cubic-bezier(0.22, 1, 0.36, 1) both;

    &--2 {
      border-color: $cy-magenta;
      box-shadow: 0 0 30rpx rgba(255, 46, 151, 0.7);
      animation-delay: 0.12s;
    }
  }

  &__core {
    width: 160rpx;
    height: 160rpx;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(0, 240, 255, 0.14);
    border: 2px solid $cy-cyan;
    box-shadow:
      0 0 40rpx rgba(0, 240, 255, 0.6),
      inset 0 0 30rpx rgba(0, 240, 255, 0.2);
  }

  &__glyph {
    font-size: 84rpx;
    font-weight: 700;
    color: $cy-cyan;
    text-shadow: 0 0 24rpx rgba(0, 240, 255, 0.9);
  }
}

/* 整个覆盖层淡出 */
@keyframes celebrate-fade {
  0% {
    opacity: 0;
  }
  15% {
    opacity: 1;
  }
  75% {
    opacity: 1;
  }
  100% {
    opacity: 0;
  }
}

/* 圆环从中心扩散出去 */
@keyframes celebrate-ring {
  0% {
    transform: scale(0.5);
    opacity: 0.9;
  }
  100% {
    transform: scale(2.1);
    opacity: 0;
  }
}

/* ── 抽卡区 ───────────────────────────────────────────────── */
.gacha {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: $fm-gap-lg 0 $fm-gap-md;

  &__head {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8rpx;
    margin-bottom: $fm-gap-md;
  }

  &__title {
    font-size: 34rpx;
    font-weight: 800;
    letter-spacing: 6rpx;
    @include neon-text($cy-cyan);
  }

  &__sub {
    @include hud-label();
  }

  &__actions {
    display: flex;
    gap: $fm-gap-md;
    width: 100%;
    margin-top: $fm-gap-lg;
  }

  &__draw {
    flex: 1;
  }

  &__eat {
    flex: 1;
  }

  &__extra {
    display: flex;
    align-items: center;
    gap: 12rpx;
    margin-top: $fm-gap-md;
  }

  &__dot {
    color: $cy-text-faint;
  }

  &__summary {
    margin-top: $fm-gap-md;
    @include hud-label();
  }

  &__relaxed {
    margin-top: 8rpx;
    font-family: $cy-mono;
    font-size: 21rpx;
    color: $cy-amber;
  }
}

/* ── 稀有度图例 ───────────────────────────────────────────── */
.legend {
  display: flex;
  gap: $fm-gap-lg;
  margin-top: $fm-gap-lg;

  &__item {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2rpx;
    opacity: 0.85;
  }

  &__key {
    font-family: $cy-mono;
    font-size: 24rpx;
    font-weight: 800;
    letter-spacing: 2rpx;
  }

  &__desc {
    font-family: $cy-mono;
    font-size: 18rpx;
    color: $cy-text-faint;
  }

  &__item--ssr &__key {
    background: linear-gradient(100deg, #00f0ff, #ff2e97, #ffc53d);
    -webkit-background-clip: text;
    background-clip: text;
    color: transparent;
  }

  &__item--sr &__key {
    color: $cy-magenta;
    text-shadow: 0 0 10rpx rgba(255, 46, 151, 0.6);
  }

  &__item--r &__key {
    color: $cy-cyan;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.6);
  }

  &__item--n &__key {
    color: $cy-text-faint;
  }
}

/* ── 榜单 ─────────────────────────────────────────────────── */
.section-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin: $fm-gap-lg 8rpx $fm-gap-md;
}

.section-title {
  font-size: 30rpx;
  font-weight: 700;
  letter-spacing: 2rpx;

  &__mark {
    color: $cy-cyan;
    text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.8);
    margin-right: 8rpx;
  }
}

.section-hint {
  font-family: $cy-mono;
  font-size: 19rpx;
  letter-spacing: 1rpx;
  color: $cy-text-faint;
}

.dish {
  position: relative;
  background: $cy-surface;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;
  padding: $fm-gap-md $fm-gap-lg;
  margin-bottom: $fm-gap-sm;
  transition: all 0.2s ease;

  &--highlight {
    border-color: $cy-cyan;
    box-shadow: 0 0 18rpx rgba(0, 240, 255, 0.35);
  }

  &__head {
    display: flex;
    align-items: center;
    gap: $fm-gap-sm;
  }

  &__rank {
    width: 46rpx;
    height: 46rpx;
    border-radius: $fm-radius-sm;
    background: $cy-surface-2;
    border: 1px solid rgba(0, 240, 255, 0.35);
    color: $cy-cyan;
    font-family: $cy-mono;
    font-size: 24rpx;
    font-weight: 700;
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
    font-weight: 700;
    letter-spacing: 1rpx;
  }

  &__meta {
    margin-top: 4rpx;
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
  }

  &__score {
    @include neon-text($cy-cyan);
    font-family: $cy-mono;
    font-size: 36rpx;
    font-weight: 800;
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
  margin-top: 12rpx;
  gap: 6rpx;
}

.reason {
  display: flex;
  align-items: flex-start;
  gap: 10rpx;

  &__mark {
    font-family: $cy-mono;
    font-size: 26rpx;
    line-height: 1.5;
    color: $cy-magenta;
    text-shadow: 0 0 10rpx rgba(255, 46, 151, 0.7);
  }

  &__text {
    flex: 1;
    font-size: 25rpx;
    line-height: 1.55;
    color: $cy-text-dim;
  }
}

.penalties {
  display: flex;
  flex-wrap: wrap;
  gap: 8rpx;
  margin-top: 10rpx;
}

.penalty {
  font-family: $cy-mono;
  font-size: 20rpx;
  color: $cy-red;
  border: 1px solid rgba(255, 59, 92, 0.35);
  border-radius: $fm-radius-sm;
  padding: 2rpx 12rpx;
}

.link {
  font-family: $cy-mono;
  font-size: 23rpx;
  color: $cy-text-faint;

  &--primary {
    color: $cy-cyan;
    font-weight: 700;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.5);
  }
}

/* ── 打分明细 ─────────────────────────────────────────────── */
.breakdown {
  margin-top: $fm-gap-md;
  padding-top: $fm-gap-md;
  border-top: 1px solid $cy-line;
}

.bar-row {
  display: flex;
  align-items: center;
  gap: 12rpx;
  margin-bottom: 12rpx;

  &__label {
    width: 140rpx;
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
    flex-shrink: 0;
  }

  &__track {
    flex: 1;
    height: 12rpx;
    border-radius: $fm-radius-pill;
    background: $cy-surface-2;
    border: 1px solid $cy-line;
    overflow: hidden;
  }

  &__fill {
    height: 100%;
    border-radius: $fm-radius-pill;
    background: linear-gradient(90deg, $cy-cyan, $cy-magenta);
    box-shadow: 0 0 12rpx rgba(0, 240, 255, 0.7);
  }

  &__value {
    width: 56rpx;
    text-align: right;
    font-family: $cy-mono;
    font-size: 21rpx;
    font-weight: 700;
    color: $cy-cyan;
  }
}

.breakdown__hint {
  display: block;
  margin-top: 10rpx;
  font-family: $cy-mono;
  font-size: 19rpx;
  line-height: 1.6;
  color: $cy-text-faint;
}

/* ── 其它 ─────────────────────────────────────────────────── */
.empty {
  padding: 80rpx 0;
  text-align: center;

  &__title {
    display: block;
    font-size: 30rpx;
    font-weight: 600;
  }

  &__desc {
    display: block;
    margin-top: 8rpx;
    font-family: $cy-mono;
    font-size: 22rpx;
    color: $cy-text-faint;
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
  @include hud-label($cy-text-faint);
  opacity: 0.7;
}
</style>
