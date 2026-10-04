<script setup lang="ts">
import { computed } from 'vue'
import type { ScoredDish } from '@/api'
import DishArt from './DishArt.vue'

/**
 * 抽卡卡片。
 *
 * 稀有度按**推荐排名**而非绝对分数划分 —— 绝对分数会随权重调整而漂移，
 * 而且冷启动时全挤在 83 分左右，按分数分档会出现"永远抽不到 SSR"。
 * 按排名分档能保证任何数据分布下都有合理的稀有度梯度。
 */
export type RarityKey = 'SSR' | 'SR' | 'R' | 'N'

interface Rarity {
  key: RarityKey
  label: string
  stars: number
}

const props = withDefaults(
  defineProps<{
    /** 抽中的菜；null 表示还没抽 */
    item: ScoredDish | null
    /** 卡面是否已翻开 */
    revealed: boolean
    /** 抽卡悬念中（牌堆抖动） */
    drawing?: boolean
    /** 可抽次数，用于显示牌堆厚度 */
    deckSize?: number
  }>(),
  { drawing: false, deckSize: 3 },
)

const emit = defineEmits<{
  (e: 'tap'): void
}>()

/** 按排名分档。 */
const rarity = computed<Rarity>(() => {
  const rank = props.item?.rank ?? 99

  if (rank === 1) return { key: 'SSR', label: '今日最佳', stars: 5 }
  if (rank <= 3) return { key: 'SR', label: '强烈推荐', stars: 4 }
  if (rank <= 8) return { key: 'R', label: '值得一试', stars: 3 }
  return { key: 'N', label: '备选', stars: 2 }
})

const stars = computed(() => '★'.repeat(rarity.value.stars) + '☆'.repeat(5 - rarity.value.stars))

const metaLine = computed(() => {
  const d = props.item?.dish
  if (!d) return ''

  const parts = [d.cuisineLabel, d.categoryLabel, d.spicyLabel]
  if (d.calories) parts.push(`${d.calories} kcal`)

  return parts.join(' · ')
})

function onTap() {
  emit('tap')
}
</script>

<template>
  <view class="stage" @tap="onTap">
    <!-- ── 牌堆（未抽时）────────────────────────────── -->
    <view v-if="!item" class="deck" :class="{ 'deck--shaking': drawing }">
      <view class="deck__back deck__back--3" />
      <view class="deck__back deck__back--2" />
      <view class="deck__back deck__back--1">
        <text class="deck__glyph">🍜</text>
        <text class="deck__hint">{{ drawing ? '抽取中…' : '点击抽一张' }}</text>
      </view>
    </view>

    <!-- ── 卡面 ─────────────────────────────────────── -->
    <view
      v-else
      class="card"
      :class="[`card--${rarity.key.toLowerCase()}`, revealed ? 'card--revealed' : 'card--hidden']"
    >
      <!-- 全息扫光 -->
      <view class="card__shine" />

      <!-- 顶部：稀有度 + 排名 -->
      <view class="card__top">
        <view class="rarity">
          <text class="rarity__key">{{ rarity.key }}</text>
          <text class="rarity__label">{{ rarity.label }}</text>
        </view>
        <text class="card__rank">#{{ String(item.rank).padStart(2, '0') }}</text>
      </view>

      <!-- 卡面视觉 -->
      <DishArt
        class="card__art"
        variant="card"
        :name="item.name"
        :cuisine="item.dish.cuisine"
        :category="item.dish.category"
        :spicy-level="item.dish.spicyLevel"
        :image-url="item.dish.imageUrl"
      />

      <!-- 菜名 -->
      <view class="card__body">
        <text class="card__name">{{ item.name }}</text>
        <view class="card__rule" />

        <view class="card__stars">
          <text class="card__stars-glyph">{{ stars }}</text>
          <text class="card__score">{{ item.score }}</text>
        </view>

        <!-- 推荐理由 -->
        <view class="card__reasons">
          <view v-for="(reason, i) in item.reasons" :key="i" class="reason">
            <text class="reason__mark">›</text>
            <text class="reason__text">{{ reason }}</text>
          </view>
        </view>
      </view>

      <!-- 底部：属性 -->
      <view class="card__foot">
        <text class="card__meta">{{ metaLine }}</text>
        <view v-if="item.penalties.length" class="card__penalties">
          <text v-for="p in item.penalties" :key="p.code" class="penalty">{{ p.description }}</text>
        </view>
      </view>
    </view>
  </view>
</template>

<style lang="scss" scoped>
.stage {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 720rpx;
  padding: $fm-gap-md 0;
}

/* ═══ 牌堆 ═══════════════════════════════════════════════════ */
.deck {
  position: relative;
  width: 520rpx;
  height: 660rpx;

  &__back {
    position: absolute;
    inset: 0;
    border-radius: $fm-radius-lg;
    background:
      repeating-linear-gradient(
        45deg,
        rgba(0, 240, 255, 0.06) 0px,
        rgba(0, 240, 255, 0.06) 8rpx,
        transparent 8rpx,
        transparent 16rpx
      ),
      $cy-surface-2;
    border: 1px solid rgba(0, 240, 255, 0.35);
    box-shadow: 0 0 18rpx rgba(0, 240, 255, 0.22);

    /* 最上面那张承载文字 */
    &--1 {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: $fm-gap-md;
    }

    &--2 {
      transform: translateY(-14rpx) scale(0.96) rotate(-1.6deg);
      opacity: 0.7;
    }

    &--3 {
      transform: translateY(-26rpx) scale(0.92) rotate(1.4deg);
      opacity: 0.45;
    }
  }

  &__glyph {
    font-size: 96rpx;
    line-height: 1;
    filter: drop-shadow(0 0 18rpx rgba(0, 240, 255, 0.6));
  }

  &__hint {
    font-family: $cy-mono;
    font-size: 24rpx;
    letter-spacing: 3rpx;
    color: $cy-cyan;
    text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.7);
    animation: fm-pulse 1.8s ease-in-out infinite;
  }

  /* 抽卡悬念：牌堆抖动 */
  &--shaking .deck__back--1 {
    animation: deck-shake 0.5s ease-in-out infinite;
  }
}

@keyframes deck-shake {
  0%,
  100% {
    transform: translateX(0) rotate(0deg);
  }
  25% {
    transform: translateX(-6rpx) rotate(-1deg);
  }
  75% {
    transform: translateX(6rpx) rotate(1deg);
  }
}

/* ═══ 卡面 ═══════════════════════════════════════════════════ */
.card {
  position: relative;
  width: 560rpx;
  min-height: 700rpx;
  padding: $fm-gap-lg;
  border-radius: $fm-radius-lg;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  background:
    linear-gradient(160deg, rgba(255, 255, 255, 0.05), transparent 40%),
    $cy-surface;
  transition:
    transform 0.55s cubic-bezier(0.22, 1, 0.36, 1),
    opacity 0.35s ease,
    box-shadow 0.55s ease;

  /* 未翻开：缩在牌堆里 */
  &--hidden {
    transform: translateY(26rpx) scale(0.86) rotate(-3deg);
    opacity: 0.5;
  }

  &--revealed {
    transform: translateY(0) scale(1) rotate(0deg);
    opacity: 1;
  }

  /* ── 稀有度配色 ────────────────────────────────── */
  &--ssr {
    border: 2px solid $cy-amber;
    box-shadow:
      0 0 26rpx rgba(255, 197, 61, 0.55),
      0 0 60rpx rgba(255, 46, 151, 0.3),
      inset 0 0 60rpx rgba(255, 197, 61, 0.08);
  }

  &--sr {
    border: 2px solid $cy-magenta;
    box-shadow:
      0 0 24rpx rgba(255, 46, 151, 0.5),
      inset 0 0 50rpx rgba(255, 46, 151, 0.07);
  }

  &--r {
    border: 2px solid $cy-cyan;
    box-shadow:
      0 0 22rpx rgba(0, 240, 255, 0.45),
      inset 0 0 50rpx rgba(0, 240, 255, 0.06);
  }

  &--n {
    border: 2px solid $cy-text-faint;
    box-shadow: 0 0 14rpx rgba(85, 85, 110, 0.4);
  }

  /* ── 全息扫光 ──────────────────────────────────── */
  &__shine {
    position: absolute;
    top: -40%;
    left: -60%;
    width: 45%;
    height: 180%;
    background: linear-gradient(
      100deg,
      transparent,
      rgba(255, 255, 255, 0.28),
      rgba(0, 240, 255, 0.18),
      transparent
    );
    transform: rotate(18deg);
    opacity: 0;
    pointer-events: none;
  }

  &--revealed &__shine {
    animation: card-shine 1s ease-out 0.12s 1;
  }

  /* ── 顶部 ──────────────────────────────────────── */
  &__top {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
  }

  &__rank {
    font-family: $cy-mono;
    font-size: 26rpx;
    font-weight: 700;
    color: $cy-text-faint;
  }

  &__art {
    margin-top: $fm-gap-md;
  }

  /* ── 主体 ──────────────────────────────────────── */
  &__body {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: $fm-gap-md 0;
  }

  &__name {
    font-size: 60rpx;
    font-weight: 800;
    letter-spacing: 4rpx;
    line-height: 1.25;
    text-align: center;
    color: $cy-text;
    text-shadow:
      0 0 24rpx rgba(0, 240, 255, 0.4),
      2rpx 0 0 rgba(255, 46, 151, 0.35),
      -2rpx 0 0 rgba(0, 240, 255, 0.35);
  }

  &__rule {
    width: 140rpx;
    height: 2px;
    margin: $fm-gap-md 0;
    background: linear-gradient(90deg, transparent, $cy-cyan, transparent);
    box-shadow: 0 0 12rpx rgba(0, 240, 255, 0.8);
  }

  &__stars {
    display: flex;
    align-items: center;
    gap: $fm-gap-md;
  }

  &__stars-glyph {
    font-size: 30rpx;
    letter-spacing: 4rpx;
    color: $cy-amber;
    text-shadow: 0 0 14rpx rgba(255, 197, 61, 0.75);
  }

  &__score {
    font-family: $cy-mono;
    font-size: 42rpx;
    font-weight: 800;
    color: $cy-cyan;
    text-shadow: 0 0 18rpx rgba(0, 240, 255, 0.7);
  }

  &__reasons {
    margin-top: $fm-gap-lg;
    display: flex;
    flex-direction: column;
    gap: 10rpx;
    width: 100%;
  }

  /* ── 底部 ──────────────────────────────────────── */
  &__foot {
    border-top: 1px solid $cy-line;
    padding-top: $fm-gap-md;
    display: flex;
    flex-direction: column;
    gap: 8rpx;
  }

  &__meta {
    font-family: $cy-mono;
    font-size: 21rpx;
    letter-spacing: 1rpx;
    color: $cy-text-faint;
    text-align: center;
  }

  &__penalties {
    display: flex;
    flex-wrap: wrap;
    justify-content: center;
    gap: 8rpx;
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

/* ═══ 稀有度徽标 ═════════════════════════════════════════════ */
.rarity {
  display: flex;
  flex-direction: column;
  gap: 4rpx;

  &__key {
    font-family: $cy-mono;
    font-size: 34rpx;
    font-weight: 800;
    letter-spacing: 3rpx;
  }

  &__label {
    font-family: $cy-mono;
    font-size: 19rpx;
    letter-spacing: 2rpx;
    color: $cy-text-faint;
  }
}

.card--ssr .rarity__key {
  background: linear-gradient(100deg, #00f0ff, #ff2e97, #ffc53d);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
  filter: drop-shadow(0 0 12rpx rgba(255, 197, 61, 0.7));
}

.card--sr .rarity__key {
  @include neon-text($cy-magenta);
}

.card--r .rarity__key {
  @include neon-text($cy-cyan);
}

.card--n .rarity__key {
  color: $cy-text-faint;
}

/* ═══ 理由 ═══════════════════════════════════════════════════ */
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
    font-size: 24rpx;
    line-height: 1.55;
    color: $cy-text-dim;
  }
}

.penalty {
  font-family: $cy-mono;
  font-size: 19rpx;
  color: $cy-red;
  border: 1px solid rgba(255, 59, 92, 0.35);
  border-radius: $fm-radius-sm;
  padding: 2rpx 12rpx;
}
</style>
