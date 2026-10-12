<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { dishApi } from '@/api'
import type { DishDetail, ScoredDish } from '@/api'
import AnimatedNumber from './AnimatedNumber.vue'
import DishArt from './DishArt.vue'

/**
 * 抽卡演出。
 *
 * 与「直接显示结果」的区别全在节奏上，七个阶段层层加码：
 *   spread   摊开牌阵，全部背面朝上，由**用户自己挑一张**
 *   lifting  选中的牌飞出牌阵、其余牌退场
 *   burst    白光爆闪 + 光芒放射
 *   banner   稀有度横幅横扫而入
 *   card     卡牌翻开落位
 *   detail   详情面板推上来（食材 / 口感 / 做法）
 *   done     定格，可操作
 *
 * 稀有度按**推荐排名**划分，保证任何数据分布下都有梯度。
 */
export type RarityKey = 'SSR' | 'SR' | 'R' | 'N'

const props = withDefaults(
  defineProps<{
    /** 牌阵：背面朝上的候选（用户不知道哪张是什么） */
    items: ScoredDish[]
    /** 已揭晓的菜；null 表示还在挑 */
    revealed: ScoredDish | null
    /** 父级正在请求 */
    drawing?: boolean
  }>(),
  { drawing: false },
)

const emit = defineEmits<{
  (e: 'pick', index: number): void
  (e: 'reset'): void
}>()

type Phase = 'spread' | 'lifting' | 'burst' | 'banner' | 'card' | 'detail' | 'done'

const phase = ref<Phase>('spread')
const pickedIndex = ref(-1)

/* ── 菜品详情（食材与做法）── */
const detail = ref<DishDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref<string | null>(null)

const rarity = computed<{ key: RarityKey; label: string; stars: number }>(() => {
  const rank = props.revealed?.rank ?? 99
  if (rank === 1) return { key: 'SSR', label: '今日最佳', stars: 5 }
  if (rank <= 3) return { key: 'SR', label: '强烈推荐', stars: 4 }
  if (rank <= 8) return { key: 'R', label: '值得一试', stars: 3 }
  return { key: 'N', label: '备选', stars: 2 }
})

const rarityClass = computed(() => `reveal--${rarity.value.key.toLowerCase()}`)
const stars = computed(() => '★'.repeat(rarity.value.stars) + '☆'.repeat(5 - rarity.value.stars))

/** 牌阵最多摊 5 张 —— 再多在小屏上就挤成一团了 */
const spread = computed(() => props.items.slice(0, 5))

/** 每张牌的扇形角度与偏移 */
function cardStyle(i: number, total: number) {
  const mid = (total - 1) / 2
  const offset = i - mid
  return {
    transform: `translateX(${offset * 78}rpx) translateY(${Math.abs(offset) * 16}rpx) rotate(${offset * 7}deg)`,
    zIndex: 10 - Math.abs(offset),
  }
}

/** 口感：从辣度、菜系、标签拼出一句话 */
const taste = computed(() => {
  const d = props.revealed?.dish
  if (!d) return ''

  const heat = ['不辣', '微辣', '小辣', '中辣', '重辣', '变态辣'][d.spicyLevel] ?? '未知'
  const parts = [`${d.cuisineLabel}风味`, heat]
  if (d.tags.length) parts.push(...d.tags.slice(0, 3))
  return parts.join(' · ')
})

const metaLine = computed(() => {
  const d = props.revealed?.dish
  if (!d) return ''
  const parts: string[] = []
  if (d.priceMinCents != null && d.priceMaxCents != null) {
    parts.push(`¥${Math.round(d.priceMinCents / 100)}–${Math.round(d.priceMaxCents / 100)}`)
  }
  if (d.calories) parts.push(`${d.calories} kcal`)
  return parts.join(' · ')
})

const timers: ReturnType<typeof setTimeout>[] = []
function after(ms: number, fn: () => void) {
  timers.push(setTimeout(fn, ms))
}
function clearTimers() {
  timers.forEach(clearTimeout)
  timers.length = 0
}

function vibrate(type: 'light' | 'medium' | 'heavy') {
  try {
    uni.vibrateShort({ type })
  } catch {
    // 不支持时忽略
  }
}

/** 用户点牌阵里的一张 */
function onPickCard(i: number) {
  if (phase.value !== 'spread' || props.drawing) return

  pickedIndex.value = i
  phase.value = 'lifting'
  vibrate('light')

  emit('pick', i)
}

/** 父级把结果递回来 → 继续演出 */
watch(
  () => props.revealed,
  (next) => {
    if (!next) return

    clearTimers()

    after(700, () => {
      phase.value = 'burst'
      vibrate('heavy')
    })
    after(1050, () => {
      phase.value = 'banner'
      vibrate('medium')
    })
    after(1750, () => {
      phase.value = 'card'
      vibrate('light')
    })
    after(2500, () => {
      phase.value = 'detail'
    })
    after(3100, () => {
      phase.value = 'done'
    })

    void loadDetail(next.dishId)
  },
)

/** 拉菜品详情，拿食材与做法 */
async function loadDetail(dishId: string) {
  detailLoading.value = true
  detailError.value = null

  try {
    detail.value = await dishApi.detail(dishId)
  } catch (err) {
    detailError.value = (err as Error).message
  } finally {
    detailLoading.value = false
  }
}

/** 再抽一次：重置回牌阵 */
function onAgain() {
  clearTimers()
  phase.value = 'spread'
  pickedIndex.value = -1
  detail.value = null
  detailError.value = null
  emit('reset')
}

defineExpose({ again: onAgain })

onUnmounted(clearTimers)
</script>

<template>
  <view class="reveal" :class="[rarityClass, `reveal--${phase}`]">
    <!-- ── 爆闪 ─────────────────────────────────────── -->
    <view v-if="phase === 'burst' || phase === 'banner'" class="reveal__flash" />

    <!-- ── 光芒放射 ─────────────────────────────────── -->
    <view v-if="phase !== 'spread' && phase !== 'lifting'" class="reveal__rays">
      <view
        v-for="i in 12"
        :key="i"
        class="reveal__ray"
        :style="{ transform: `rotate(${i * 30}deg)` }"
      />
    </view>

    <!-- ══ 牌阵：全部背面朝上，由用户挑 ═══════════════ -->
    <view v-if="phase === 'spread' || phase === 'lifting'" class="spread">
      <text class="spread__hint">
        {{ phase === 'lifting' ? '揭晓中…' : '挑一张' }}
      </text>

      <view class="spread__deck">
        <view
          v-for="(item, i) in spread"
          :key="item.dishId"
          class="back"
          :class="{
            'back--picked': pickedIndex === i,
            'back--dimmed': pickedIndex >= 0 && pickedIndex !== i,
          }"
          :style="cardStyle(i, spread.length)"
          hover-class="hover-dim"
          @tap="onPickCard(i)"
        >
          <view class="back__face">
            <text class="back__glyph">🍜</text>
            <view class="back__lines">
              <view v-for="n in 3" :key="n" class="back__line" />
            </view>
          </view>
        </view>
      </view>

      <text class="spread__sub">从 {{ spread.length }} 张里选一张</text>
    </view>

    <!-- ══ 稀有度横幅 ═════════════════════════════════ -->
    <view v-if="phase === 'banner' || phase === 'card' || phase === 'detail' || phase === 'done'" class="banner">
      <text class="banner__stars">{{ stars }}</text>
      <text class="banner__key">{{ rarity.key }}</text>
      <text class="banner__label">{{ rarity.label }}</text>
    </view>

    <!-- ══ 卡牌 ═══════════════════════════════════════ -->
    <view
      v-if="(phase === 'card' || phase === 'detail' || phase === 'done') && revealed"
      class="card"
      :class="{ 'card--settled': phase !== 'card' }"
    >
      <view class="card__shine" />

      <view class="card__head">
        <text class="card__rank">#{{ String(revealed.rank).padStart(2, '0') }}</text>
        <text class="card__rarity">{{ rarity.key }}</text>
      </view>

      <DishArt
        class="card__art"
        variant="card"
        :name="revealed.name"
        :cuisine="revealed.dish.cuisine"
        :category="revealed.dish.category"
        :spicy-level="revealed.dish.spicyLevel"
        :image-url="revealed.dish.imageUrl"
      />

      <text class="card__name">{{ revealed.name }}</text>
      <view class="card__rule" />

      <view class="card__score-row">
        <AnimatedNumber class="card__score" :value="revealed.score" :decimals="1" :duration="900" />
        <text class="card__score-unit">分</text>
      </view>

      <view class="card__reasons">
        <view v-for="(r, i) in revealed.reasons" :key="i" class="card__reason">
          <text class="card__reason-mark">›</text>
          <text class="card__reason-text">{{ r }}</text>
        </view>
      </view>

      <text v-if="metaLine" class="card__meta">{{ metaLine }}</text>
    </view>

    <!-- ══ 详情：食材 / 口感 / 做法 ═══════════════════ -->
    <view
      v-if="phase === 'detail' || phase === 'done'"
      class="detail anim-in"
    >
      <!-- 口感 -->
      <view class="detail__block">
        <view class="detail__head">
          <text class="detail__mark">TASTE</text>
          <text class="detail__title">口感</text>
        </view>
        <text class="detail__taste">{{ taste }}</text>
      </view>

      <!-- 食材 -->
      <view class="detail__block">
        <view class="detail__head">
          <text class="detail__mark">NEED</text>
          <text class="detail__title">需要什么</text>
        </view>

        <text v-if="detailLoading" class="detail__loading">读取中…</text>
        <text v-else-if="detailError" class="detail__err">{{ detailError }}</text>
        <text v-else-if="!detail?.ingredients.length" class="detail__loading">
          这道菜还没录入食材信息
        </text>

        <view v-else class="ings">
          <view v-for="(ing, i) in detail.ingredients" :key="i" class="ing">
            <text class="ing__name">
              {{ ing.name }}
              <text v-if="ing.isCommonAllergen" class="ing__allergen">过敏原</text>
            </text>
            <text class="ing__amount">{{ ing.amount ?? '适量' }}</text>
          </view>
        </view>
      </view>

      <!-- 做法 -->
      <view class="detail__block">
        <view class="detail__head">
          <text class="detail__mark">HOW</text>
          <text class="detail__title">怎么做</text>
          <text v-if="detail?.recipe" class="detail__badge">
            {{ detail.recipe.servings }} 人份 · {{ detail.recipe.cookMinutes }} 分钟 ·
            {{ detail.recipe.difficultyLabel }}
          </text>
        </view>

        <text v-if="detailLoading" class="detail__loading">读取中…</text>

        <template v-else-if="detail?.recipe">
          <view class="steps">
            <view v-for="s in detail.recipe.steps" :key="s.order" class="step">
              <view class="step__no">{{ s.order }}</view>
              <view class="step__body">
                <text class="step__text">{{ s.text }}</text>
                <text v-if="s.durationMinutes" class="step__time">约 {{ s.durationMinutes }} 分钟</text>
              </view>
            </view>
          </view>

          <view v-if="detail.recipe.tips" class="tips">
            <text class="tips__label">小贴士</text>
            <text class="tips__text">{{ detail.recipe.tips }}</text>
          </view>
        </template>

        <text v-else class="detail__loading">
          这道菜还没有做法。去菜品详情页可以一键让 AI 生成。
        </text>
      </view>
    </view>

    <!-- ══ 底部提示 ═══════════════════════════════════ -->
    <text v-if="phase === 'done'" class="reveal__congrats anim-pop">— 抽到了 —</text>
  </view>
</template>

<style lang="scss" scoped>
.reveal {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  overflow: hidden;
  padding-bottom: $fm-gap-lg;

  /* 稀有度色 */
  &--n {
    --r: #6b7a94;
  }
  &--r {
    --r: #00f0ff;
  }
  &--sr {
    --r: #ff2e97;
  }
  &--ssr {
    --r: #ffc53d;
  }

  /* 牌阵阶段要撑高，避免布局跳动 */
  &--spread,
  &--lifting {
    min-height: 720rpx;
    justify-content: center;
  }
}

/* ═══ 牌阵 ═══════════════════════════════════════════════════ */
.spread {
  display: flex;
  flex-direction: column;
  align-items: center;
  width: 100%;

  &__hint {
    font-family: $cy-mono;
    font-size: 26rpx;
    letter-spacing: 6rpx;
    color: $cy-cyan;
    text-shadow: 0 0 14rpx rgba(0, 240, 255, 0.9);
    animation: fm-pulse 1.8s ease-in-out infinite;
  }

  &__deck {
    position: relative;
    height: 520rpx;
    width: 100%;
    margin-top: $fm-gap-lg;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  &__sub {
    margin-top: $fm-gap-md;
    @include hud-label();
  }
}

/* ── 背面牌 ───────────────────────────────────────────────── */
.back {
  position: absolute;
  width: 260rpx;
  height: 400rpx;
  border-radius: $fm-radius-md;
  transition:
    transform 0.42s cubic-bezier(0.22, 1, 0.36, 1),
    opacity 0.3s ease,
    box-shadow 0.3s ease;

  &__face {
    width: 100%;
    height: 100%;
    border-radius: $fm-radius-md;
    background:
      repeating-linear-gradient(
        45deg,
        rgba(0, 240, 255, 0.08) 0px,
        rgba(0, 240, 255, 0.08) 8rpx,
        transparent 8rpx,
        transparent 18rpx
      ),
      linear-gradient(160deg, #12203a, #06060f);
    border: 2px solid rgba(0, 240, 255, 0.5);
    box-shadow:
      0 0 6rpx rgba(0, 240, 255, 0.6),
      0 0 22rpx rgba(0, 240, 255, 0.25);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 18rpx;
  }

  &__glyph {
    font-size: 76rpx;
    filter: drop-shadow(0 0 16rpx rgba(0, 240, 255, 0.8));
    opacity: 0.85;
  }

  &__lines {
    display: flex;
    flex-direction: column;
    gap: 8rpx;
    width: 60%;
  }

  &__line {
    height: 3rpx;
    border-radius: 999rpx;
    background: rgba(0, 240, 255, 0.35);
  }

  /* 选中的那张：飞出牌阵、放大 */
  &--picked {
    transform: translateY(-60rpx) scale(1.16) rotate(0deg) !important;
    z-index: 40 !important;
    box-shadow:
      0 0 12rpx rgba(0, 240, 255, 0.9),
      0 0 50rpx rgba(0, 240, 255, 0.5);
  }

  /* 其余牌：退场 */
  &--dimmed {
    opacity: 0;
    transform: translateY(60rpx) scale(0.8) !important;
  }
}

/* ═══ 爆闪 ═══════════════════════════════════════════════════ */
.reveal__flash {
  position: absolute;
  inset: 0;
  background: radial-gradient(circle at 50% 40%, #fff 0%, rgba(255, 255, 255, 0.5) 35%, transparent 72%);
  animation: flash 0.75s ease-out both;
  pointer-events: none;
  z-index: 30;
}

@keyframes flash {
  0% {
    opacity: 0;
  }
  12% {
    opacity: 1;
  }
  100% {
    opacity: 0;
  }
}

/* ═══ 光芒 ═══════════════════════════════════════════════════ */
.reveal__rays {
  position: absolute;
  top: 60rpx;
  width: 1100rpx;
  height: 1100rpx;
  animation: rays-spin 14s linear infinite;
  pointer-events: none;
  opacity: 0.45;
}

.reveal__ray {
  position: absolute;
  left: 50%;
  top: 0;
  width: 3rpx;
  height: 550rpx;
  margin-left: -1.5rpx;
  transform-origin: 50% 100%;
  background: linear-gradient(180deg, transparent, var(--r));
  opacity: 0.35;
}

@keyframes rays-spin {
  to {
    transform: rotate(360deg);
  }
}

/* ═══ 稀有度横幅 ═════════════════════════════════════════════ */
.banner {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4rpx;
  padding: $fm-gap-md 0;
  z-index: 20;
  animation: banner-in 0.5s cubic-bezier(0.22, 1, 0.36, 1) both;

  &__stars {
    font-size: 34rpx;
    letter-spacing: 8rpx;
    color: var(--r);
    text-shadow: 0 0 18rpx var(--r);
  }

  &__key {
    font-family: $cy-mono;
    font-size: 68rpx;
    font-weight: 900;
    letter-spacing: 12rpx;
    color: var(--r);
    text-shadow:
      0 0 8rpx var(--r),
      0 0 30rpx var(--r),
      0 0 70rpx var(--r);
  }

  &__label {
    font-family: $cy-mono;
    font-size: 21rpx;
    letter-spacing: 4rpx;
    color: $cy-text-dim;
  }
}

@keyframes banner-in {
  0% {
    opacity: 0;
    transform: translateY(-40rpx) scale(0.7);
  }
  60% {
    opacity: 1;
    transform: translateY(0) scale(1.08);
  }
  100% {
    transform: scale(1);
  }
}

/* ═══ 卡牌 ═══════════════════════════════════════════════════ */
.card {
  position: relative;
  width: 580rpx;
  padding: $fm-gap-lg;
  border-radius: $fm-radius-lg;
  background:
    linear-gradient(160deg, rgba(255, 255, 255, 0.06), transparent 42%),
    $cy-surface;
  border: 2px solid var(--r);
  box-shadow:
    0 0 8rpx var(--r),
    0 0 32rpx rgba(255, 255, 255, 0.14),
    inset 0 0 60rpx rgba(255, 255, 255, 0.05);
  display: flex;
  flex-direction: column;
  align-items: center;
  overflow: hidden;
  z-index: 15;
  animation: card-drop 0.72s cubic-bezier(0.22, 1, 0.36, 1) both;

  &--settled {
    animation: none;
  }

  &__shine {
    position: absolute;
    top: -40%;
    left: -60%;
    width: 45%;
    height: 180%;
    background: linear-gradient(100deg, transparent, rgba(255, 255, 255, 0.4), transparent);
    transform: rotate(18deg);
    animation: card-shine 1.1s ease-out 0.3s 1 both;
    pointer-events: none;
  }

  &__head {
    width: 100%;
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: $fm-gap-md;
  }

  &__rank {
    font-family: $cy-mono;
    font-size: 24rpx;
    color: $cy-text-faint;
  }

  &__rarity {
    font-family: $cy-mono;
    font-size: 26rpx;
    font-weight: 800;
    letter-spacing: 3rpx;
    color: var(--r);
    text-shadow: 0 0 12rpx var(--r);
  }

  &__art {
    width: 100%;
  }

  &__name {
    margin-top: $fm-gap-md;
    font-size: 54rpx;
    font-weight: 800;
    letter-spacing: 4rpx;
    color: $cy-text;
    text-shadow:
      0 0 20rpx rgba(0, 240, 255, 0.5),
      2rpx 0 0 rgba(255, 46, 151, 0.4);
  }

  &__rule {
    width: 160rpx;
    height: 2px;
    margin: $fm-gap-md 0;
    background: linear-gradient(90deg, transparent, var(--r), transparent);
    box-shadow: 0 0 14rpx var(--r);
  }

  &__score-row {
    display: flex;
    align-items: baseline;
    gap: 8rpx;
  }

  &__score {
    font-size: 54rpx;
    font-weight: 900;
    color: var(--r);
    text-shadow: 0 0 18rpx var(--r);
  }

  &__score-unit {
    font-family: $cy-mono;
    font-size: 22rpx;
    color: $cy-text-faint;
  }

  &__reasons {
    width: 100%;
    margin-top: $fm-gap-lg;
    display: flex;
    flex-direction: column;
    gap: 8rpx;
  }

  &__reason {
    display: flex;
    gap: 10rpx;
  }

  &__reason-mark {
    font-family: $cy-mono;
    font-size: 26rpx;
    color: $cy-magenta;
    text-shadow: 0 0 10rpx rgba(255, 46, 151, 0.8);
  }

  &__reason-text {
    flex: 1;
    font-size: 25rpx;
    line-height: 1.55;
    color: $cy-text-dim;
  }

  &__meta {
    margin-top: $fm-gap-md;
    padding-top: $fm-gap-md;
    border-top: 1px solid rgba(255, 255, 255, 0.1);
    width: 100%;
    text-align: center;
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
  }
}

@keyframes card-drop {
  0% {
    opacity: 0;
    transform: translateY(120rpx) scale(0.55) rotate(-8deg);
  }
  65% {
    opacity: 1;
    transform: translateY(0) scale(1.05) rotate(1deg);
  }
  100% {
    transform: translateY(0) scale(1) rotate(0deg);
  }
}

@keyframes card-shine {
  0% {
    transform: translateX(0) rotate(18deg);
    opacity: 0;
  }
  25% {
    opacity: 1;
  }
  100% {
    transform: translateX(420%) rotate(18deg);
    opacity: 0;
  }
}

/* ═══ 详情 ═══════════════════════════════════════════════════ */
.detail {
  width: 100%;
  margin-top: $fm-gap-lg;
  display: flex;
  flex-direction: column;
  gap: $fm-gap-md;

  &__block {
    padding: $fm-gap-md $fm-gap-lg;
    background: $cy-surface;
    border: 1px solid rgba(0, 240, 255, 0.22);
    border-radius: $fm-radius-md;
  }

  &__head {
    display: flex;
    align-items: baseline;
    gap: 12rpx;
    margin-bottom: $fm-gap-md;
  }

  &__mark {
    font-family: $cy-mono;
    font-size: 18rpx;
    font-weight: 700;
    color: $cy-void;
    background: $cy-cyan;
    padding: 2rpx 10rpx;
    border-radius: $fm-radius-sm;
  }

  &__title {
    font-size: 28rpx;
    font-weight: 700;
    letter-spacing: 2rpx;
  }

  &__badge {
    margin-left: auto;
    font-family: $cy-mono;
    font-size: 19rpx;
    color: $cy-text-faint;
  }

  &__taste {
    font-size: 27rpx;
    line-height: 1.7;
    color: $cy-text-dim;
  }

  &__loading,
  &__err {
    font-family: $cy-mono;
    font-size: 22rpx;
    color: $cy-text-faint;
  }

  &__err {
    color: $cy-red;
  }
}

/* ── 食材 ─────────────────────────────────────────────────── */
.ings {
  display: flex;
  flex-direction: column;
}

.ing {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14rpx 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);

  &:last-child {
    border-bottom: none;
  }

  &__name {
    font-size: 26rpx;
    color: $cy-text;
  }

  &__allergen {
    margin-left: 10rpx;
    font-size: 19rpx;
    color: $cy-amber;
    background: rgba(255, 197, 61, 0.14);
    border-radius: $fm-radius-sm;
    padding: 2rpx 10rpx;
  }

  &__amount {
    font-family: $cy-mono;
    font-size: 23rpx;
    color: $cy-text-faint;
  }
}

/* ── 做法 ─────────────────────────────────────────────────── */
.steps {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-md;
}

.step {
  display: flex;
  gap: $fm-gap-md;

  &__no {
    width: 42rpx;
    height: 42rpx;
    flex-shrink: 0;
    border-radius: $fm-radius-sm;
    background: rgba(0, 240, 255, 0.14);
    border: 1px solid $cy-cyan;
    color: $cy-cyan;
    font-family: $cy-mono;
    font-size: 23rpx;
    font-weight: 700;
    display: flex;
    align-items: center;
    justify-content: center;
    box-shadow: 0 0 10rpx rgba(0, 240, 255, 0.35);
  }

  &__body {
    flex: 1;
    display: flex;
    flex-direction: column;
  }

  &__text {
    font-size: 26rpx;
    line-height: 1.7;
    color: $cy-text-dim;
  }

  &__time {
    margin-top: 4rpx;
    font-family: $cy-mono;
    font-size: 19rpx;
    color: $cy-text-faint;
  }
}

.tips {
  margin-top: $fm-gap-lg;
  padding: $fm-gap-md;
  border-radius: $fm-radius-md;
  background: rgba(0, 240, 255, 0.07);
  border-left: 3px solid $cy-cyan;

  &__label {
    display: block;
    font-family: $cy-mono;
    font-size: 20rpx;
    letter-spacing: 2rpx;
    color: $cy-cyan;
    margin-bottom: 6rpx;
  }

  &__text {
    font-size: 25rpx;
    line-height: 1.7;
    color: $cy-text-dim;
  }
}

/* ═══ 完成提示 ═══════════════════════════════════════════════ */
.reveal__congrats {
  margin-top: $fm-gap-lg;
  font-family: $cy-mono;
  font-size: 24rpx;
  letter-spacing: 6rpx;
  color: var(--r);
  text-shadow: 0 0 14rpx var(--r);
}
</style>
