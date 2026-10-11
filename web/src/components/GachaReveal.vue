<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import type { ScoredDish } from '@/api'
import AnimatedNumber from './AnimatedNumber.vue'
import DishArt from './DishArt.vue'

/**
 * 抽卡演出。
 *
 * 六个阶段，层层加码 —— 这是「抽卡」和「直接显示结果」的全部区别：
 *   idle    卡包悬浮待命
 *   shaking 卡包剧烈抖动（悬念）
 *   burst   白光爆闪 + 光芒放射（揭晓前的一瞬）
 *   banner  稀有度横幅横扫而入
 *   card    卡牌落位、翻开
 *   done    定格，可操作
 *
 * 稀有度按**推荐排名**划分，保证任何数据分布下都有梯度。
 */
export type RarityKey = 'SSR' | 'SR' | 'R' | 'N'

const props = defineProps<{
  /** 抽中的菜；null 表示还没抽 */
  item: ScoredDish | null
  /** 抽卡请求中 */
  drawing?: boolean
}>()

const emit = defineEmits<{
  (e: 'draw'): void
}>()

type Phase = 'idle' | 'shaking' | 'burst' | 'banner' | 'card' | 'done'

const phase = ref<Phase>('idle')
const pending = ref<ScoredDish | null>(null)

const rarity = computed<{ key: RarityKey; label: string; stars: number }>(() => {
  const rank = (pending.value ?? props.item)?.rank ?? 99
  if (rank === 1) return { key: 'SSR', label: '今日最佳', stars: 5 }
  if (rank <= 3) return { key: 'SR', label: '强烈推荐', stars: 4 }
  if (rank <= 8) return { key: 'R', label: '值得一试', stars: 3 }
  return { key: 'N', label: '备选', stars: 2 }
})

const rarityClass = computed(() => `reveal--${rarity.value.key.toLowerCase()}`)

const stars = computed(() => '★'.repeat(rarity.value.stars) + '☆'.repeat(5 - rarity.value.stars))

/** 是否处于演出中（禁止重复点击） */
const busy = computed(() => phase.value !== 'idle' && phase.value !== 'done')

const metaLine = computed(() => {
  const d = pending.value?.dish
  if (!d) return ''
  const parts = [d.cuisineLabel, d.categoryLabel, d.spicyLabel]
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

/** 外部把结果递进来 → 开始演出 */
watch(
  () => props.item,
  (next) => {
    if (!next) return

    clearTimers()
    pending.value = next
    phase.value = 'shaking'

    // 悬念：抖动 → 爆闪 → 横幅 → 落牌
    after(900, () => {
      phase.value = 'burst'
      vibrate('heavy')
    })
    after(1250, () => {
      phase.value = 'banner'
      vibrate('medium')
    })
    after(1950, () => {
      phase.value = 'card'
      vibrate('light')
    })
    after(2750, () => {
      phase.value = 'done'
    })
  },
)

function vibrate(type: 'light' | 'medium' | 'heavy') {
  try {
    uni.vibrateShort({ type })
  } catch {
    // 不支持时忽略
  }
}

function onPackTap() {
  if (busy.value || props.drawing) return
  emit('draw')
}

onUnmounted(clearTimers)
</script>

<template>
  <view class="reveal" :class="[rarityClass, `reveal--${phase}`]">
    <!-- ── 全屏爆闪 ─────────────────────────────────── -->
    <view v-if="phase === 'burst' || phase === 'banner'" class="reveal__flash" />

    <!-- ── 光芒放射 ─────────────────────────────────── -->
    <view v-if="phase !== 'idle' && phase !== 'shaking'" class="reveal__rays">
      <view v-for="i in 12" :key="i" class="reveal__ray" :style="{ transform: `rotate(${i * 30}deg)` }" />
    </view>

    <!-- ── 卡包（未抽）──────────────────────────────── -->
    <view
      v-if="phase === 'idle' || phase === 'shaking'"
      class="pack"
      :class="{ 'pack--shaking': phase === 'shaking' || drawing }"
      hover-class="hover-dim"
      @tap="onPackTap"
    >
      <view class="pack__inner">
        <text class="pack__glyph">🍜</text>
        <text class="pack__title">今日卡包</text>
        <text class="pack__hint">{{ drawing ? '抽取中…' : '点击开启' }}</text>
      </view>
      <view class="pack__glow" />
    </view>

    <!-- ── 稀有度横幅 ───────────────────────────────── -->
    <view v-if="phase === 'banner' || phase === 'card' || phase === 'done'" class="banner">
      <text class="banner__stars">{{ stars }}</text>
      <text class="banner__key">{{ rarity.key }}</text>
      <text class="banner__label">{{ rarity.label }}</text>
    </view>

    <!-- ── 卡牌 ─────────────────────────────────────── -->
    <view
      v-if="(phase === 'card' || phase === 'done') && pending"
      class="card"
      :class="{ 'card--settled': phase === 'done' }"
    >
      <view class="card__shine" />

      <view class="card__head">
        <text class="card__rank">#{{ String(pending.rank).padStart(2, '0') }}</text>
        <text class="card__rarity">{{ rarity.key }}</text>
      </view>

      <DishArt
        class="card__art"
        variant="card"
        :name="pending.name"
        :cuisine="pending.dish.cuisine"
        :category="pending.dish.category"
        :spicy-level="pending.dish.spicyLevel"
        :image-url="pending.dish.imageUrl"
      />

      <text class="card__name">{{ pending.name }}</text>
      <view class="card__rule" />

      <AnimatedNumber
        class="card__score"
        :value="pending.score"
        :decimals="1"
        :duration="900"
      />

      <view class="card__reasons">
        <view v-for="(r, i) in pending.reasons" :key="i" class="card__reason">
          <text class="card__reason-mark">›</text>
          <text class="card__reason-text">{{ r }}</text>
        </view>
      </view>

      <text class="card__meta">{{ metaLine }}</text>
    </view>

    <!-- ── 完成提示 ─────────────────────────────────── -->
    <text v-if="phase === 'done'" class="reveal__congrats anim-pop">
      — 抽到了 —
    </text>
  </view>
</template>

<style lang="scss" scoped>
.reveal {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 820rpx;
  overflow: hidden;

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
}

/* ── 爆闪 ─────────────────────────────────────────────────── */
.reveal__flash {
  position: absolute;
  inset: 0;
  background: radial-gradient(circle at 50% 45%, #fff 0%, rgba(255, 255, 255, 0.5) 35%, transparent 72%);
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

/* ── 光芒放射 ─────────────────────────────────────────────── */
.reveal__rays {
  position: absolute;
  width: 1200rpx;
  height: 1200rpx;
  animation: rays-spin 14s linear infinite;
  pointer-events: none;
  opacity: 0.5;
}

.reveal__ray {
  position: absolute;
  left: 50%;
  top: 0;
  width: 3rpx;
  height: 600rpx;
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

/* ── 卡包 ─────────────────────────────────────────────────── */
.pack {
  position: relative;
  width: 440rpx;
  height: 600rpx;
  border-radius: $fm-radius-lg;
  background:
    repeating-linear-gradient(
      45deg,
      rgba(0, 240, 255, 0.07) 0px,
      rgba(0, 240, 255, 0.07) 10rpx,
      transparent 10rpx,
      transparent 22rpx
    ),
    linear-gradient(160deg, #12203a, #06060f);
  border: 2px solid rgba(0, 240, 255, 0.6);
  box-shadow:
    0 0 6rpx rgba(0, 240, 255, 0.9),
    0 0 30rpx rgba(0, 240, 255, 0.45),
    0 0 70rpx rgba(0, 240, 255, 0.2),
    inset 0 0 50rpx rgba(0, 240, 255, 0.1);
  animation: pack-float 3.2s ease-in-out infinite;
  z-index: 10;

  &__inner {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 18rpx;
  }

  &__glyph {
    font-size: 120rpx;
    filter: drop-shadow(0 0 24rpx rgba(0, 240, 255, 0.9));
  }

  &__title {
    font-family: $cy-mono;
    font-size: 26rpx;
    letter-spacing: 6rpx;
    color: $cy-cyan;
    text-shadow: 0 0 14rpx rgba(0, 240, 255, 0.9);
  }

  &__hint {
    font-family: $cy-mono;
    font-size: 22rpx;
    letter-spacing: 3rpx;
    color: $cy-text-faint;
    animation: fm-pulse 1.6s ease-in-out infinite;
  }

  &__glow {
    position: absolute;
    inset: -40rpx;
    border-radius: 50%;
    background: radial-gradient(circle, rgba(0, 240, 255, 0.22), transparent 68%);
    filter: blur(20px);
    pointer-events: none;
  }

  /* 抖动：横向位移 + 旋转 + 缩放，像卡包要炸开 */
  &--shaking {
    animation: pack-shake 0.16s ease-in-out infinite;
  }
}

@keyframes pack-float {
  0%,
  100% {
    transform: translateY(0) rotate(-1deg);
  }
  50% {
    transform: translateY(-18rpx) rotate(1deg);
  }
}

@keyframes pack-shake {
  0% {
    transform: translate(0, 0) rotate(0deg) scale(1);
  }
  25% {
    transform: translate(-8rpx, 4rpx) rotate(-2deg) scale(1.02);
  }
  50% {
    transform: translate(6rpx, -6rpx) rotate(2deg) scale(0.99);
  }
  75% {
    transform: translate(-4rpx, -3rpx) rotate(-1.5deg) scale(1.03);
  }
  100% {
    transform: translate(0, 0) rotate(0deg) scale(1);
  }
}

/* ── 稀有度横幅 ───────────────────────────────────────────── */
.banner {
  position: absolute;
  top: 96rpx;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6rpx;
  z-index: 20;
  animation: banner-in 0.5s cubic-bezier(0.22, 1, 0.36, 1) both;

  &__stars {
    font-size: 40rpx;
    letter-spacing: 8rpx;
    color: var(--r);
    text-shadow: 0 0 18rpx var(--r);
  }

  &__key {
    font-family: $cy-mono;
    font-size: 76rpx;
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
    font-size: 22rpx;
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

/* ── 卡牌 ─────────────────────────────────────────────────── */
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

  /* 落位：从下方冲出并放大 */
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
    background: linear-gradient(
      100deg,
      transparent,
      rgba(255, 255, 255, 0.4),
      transparent
    );
    transform: rotate(18deg);
    animation: card-shine 1.1s ease-out 0.35s 1 both;
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
    font-size: 56rpx;
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

  &__score {
    font-size: 56rpx;
    font-weight: 900;
    color: var(--r);
    text-shadow: 0 0 18rpx var(--r);
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
    margin-top: $fm-gap-lg;
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

/* ── 完成提示 ─────────────────────────────────────────────── */
.reveal__congrats {
  margin-top: $fm-gap-lg;
  font-family: $cy-mono;
  font-size: 24rpx;
  letter-spacing: 6rpx;
  color: var(--r);
  text-shadow: 0 0 14rpx var(--r);
}
</style>
