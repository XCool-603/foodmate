<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { dishApi } from '@/api'
import type { DishDetail, ScoredDish } from '@/api'
import AnimatedNumber from './AnimatedNumber.vue'
import DishArt from './DishArt.vue'

/**
 * 抽卡演出（自动）。
 *
 * 用户不挑牌 —— 点「开始抽」之后由系统自动选中一张，
 * 过程用「老虎机」式的光标滚动来制造悬念。
 *
 * 阶段：
 *   deck    牌阵摊开，全部背面朝上
 *   rolling 光标在牌间高速滚动，逐渐减速（悬念）
 *   lifting 光标落定，那张牌飞出牌阵
 *   burst   白光爆闪 + 光芒放射
 *   banner  稀有度横幅横扫而入
 *   card    卡牌翻开落位
 *   detail  详情推上来（口感 / 食材 / 做法）
 *   done    定格
 */
export type RarityKey = 'SSR' | 'SR' | 'R' | 'N'

const props = withDefaults(
  defineProps<{
    /** 牌阵候选 */
    items: ScoredDish[]
    /** 已揭晓的菜；null 表示还在抽 */
    revealed: ScoredDish | null
  }>(),
  {},
)

const emit = defineEmits<{
  (e: 'pick', index: number): void
}>()

type Phase = 'deck' | 'rolling' | 'lifting' | 'burst' | 'banner' | 'card' | 'detail' | 'done'

const phase = ref<Phase>('deck')
/** 光标当前停在牌阵的第几张 */
const cursor = ref(0)
/** 最终落定的那张 */
const landed = ref(-1)

/* ── 详情 ── */
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

/** 牌阵最多摊 5 张 */
const spread = computed(() => props.items.slice(0, 5))

const showDeck = computed(() => phase.value === 'deck' || phase.value === 'rolling' || phase.value === 'lifting')
const showCard = computed(
  () => phase.value === 'card' || phase.value === 'detail' || phase.value === 'done',
)
const showBanner = computed(
  () => phase.value === 'banner' || showCard.value,
)
const showDetail = computed(() => phase.value === 'detail' || phase.value === 'done')

/** 口感：菜系 + 辣度 + 标签 */
const taste = computed(() => {
  const d = props.revealed?.dish
  if (!d) return ''
  const heat = ['不辣', '微辣', '小辣', '中辣', '重辣', '变态辣'][d.spicyLevel] ?? '未知'
  const parts = [`${d.cuisineLabel}风味`, heat, ...d.tags.slice(0, 3)]
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

/* ── 定时器 ── */
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

/**
 * 开始抽卡：光标在牌间滚动并减速，最后落在随机一张上。
 *
 * 减速曲线用 progress² —— 前段飞快、后段拖慢，
 * 这是老虎机让人上瘾的节奏。
 */
function start() {
  if (spread.value.length === 0 || phase.value !== 'deck') return

  const total = spread.value.length
  const target = Math.floor(Math.random() * total)
  const laps = 3
  const totalSteps = total * laps + target

  phase.value = 'rolling'

  let step = 0

  const tick = () => {
    cursor.value = step % total
    step++

    if (step > totalSteps) {
      cursor.value = target
      landed.value = target

      // 落定 → 抬牌
      after(320, () => {
        phase.value = 'lifting'
        vibrate('light')
      })

      // 抬牌 → 爆闪
      after(1000, () => {
        phase.value = 'burst'
        vibrate('heavy')
      })

      // 爆闪 → 横幅
      after(1350, () => {
        phase.value = 'banner'
        vibrate('medium')
      })

      // 横幅 → 落牌
      after(2050, () => {
        phase.value = 'card'
        vibrate('light')
      })

      // 落牌 → 详情
      after(2800, () => {
        phase.value = 'detail'
      })

      after(3400, () => {
        phase.value = 'done'
      })

      return
    }

    const progress = step / totalSteps
    const delay = 55 + progress * progress * 260
    timers.push(setTimeout(tick, delay))
  }

  tick()

  // 通知父级解析落定的那张
  emit('pick', target)
}

/** 父级把结果递回来 → 拉详情 */
watch(
  () => props.revealed,
  (next) => {
    if (next) void loadDetail(next.dishId)
  },
)

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

/** 重置回牌阵（供「再抽一次」调用） */
function reset() {
  clearTimers()
  phase.value = 'deck'
  cursor.value = 0
  landed.value = -1
  detail.value = null
  detailError.value = null
}

defineExpose({ start, reset })

onMounted(() => {
  // 进页面稍等一下再开始，让用户先看清牌阵
  after(600, start)
})

onUnmounted(clearTimers)
</script>

<template>
  <view class="reveal" :class="[rarityClass, `reveal--${phase}`]">
    <!-- ══ 光芒（只在演出中）═════════════════════════ -->
    <view v-if="phase !== 'deck' && phase !== 'rolling'" class="rays">
      <view class="rays__spin">
        <view
          v-for="i in 12"
          :key="i"
          class="rays__line"
          :style="{ transform: `rotate(${i * 30}deg)` }"
        />
      </view>
    </view>

    <!-- ══ 爆闪 ═══════════════════════════════════════ -->
    <view v-if="phase === 'burst' || phase === 'banner'" class="flash" />

    <!-- ══ 牌阵 ═══════════════════════════════════════ -->
    <view v-if="showDeck" class="deck">
      <text class="deck__hint">
        {{ phase === 'deck' ? '准备抽卡…' : phase === 'rolling' ? '抽卡中…' : '揭晓！' }}
      </text>

      <view class="deck__row">
        <view
          v-for="(item, i) in spread"
          :key="item.dishId"
          class="back"
          :class="{
            'back--cursor': cursor === i && phase !== 'lifting',
            'back--landed': landed === i,
            'back--out': landed >= 0 && landed !== i,
          }"
        >
          <text class="back__glyph">🍜</text>
          <view class="back__lines">
            <view v-for="n in 3" :key="n" class="back__line" />
          </view>
        </view>
      </view>

      <text class="deck__sub">{{ spread.length }} 张候选 · 系统为你抽</text>
    </view>

    <!-- ══ 稀有度横幅 ═════════════════════════════════ -->
    <view v-if="showBanner" class="banner">
      <text class="banner__stars">{{ stars }}</text>
      <text class="banner__key">{{ rarity.key }}</text>
      <text class="banner__label">{{ rarity.label }}</text>
    </view>

    <!-- ══ 卡牌 ═══════════════════════════════════════ -->
    <view v-if="showCard && revealed" class="card" :class="{ 'card--settled': phase !== 'card' }">
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
        <text class="card__unit">分</text>
      </view>

      <view class="card__reasons">
        <view v-for="(r, i) in revealed.reasons" :key="i" class="card__reason">
          <text class="card__reason-mark">›</text>
          <text class="card__reason-text">{{ r }}</text>
        </view>
      </view>

      <text v-if="metaLine" class="card__meta">{{ metaLine }}</text>
    </view>

    <!-- ══ 详情 ═══════════════════════════════════════ -->
    <view v-if="showDetail" class="detail anim-in">
      <!-- 口感 -->
      <view class="block">
        <view class="block__head">
          <text class="block__mark">TASTE</text>
          <text class="block__title">口感</text>
        </view>
        <text class="block__text">{{ taste }}</text>
      </view>

      <!-- 食材 -->
      <view class="block">
        <view class="block__head">
          <text class="block__mark">NEED</text>
          <text class="block__title">需要什么</text>
        </view>

        <text v-if="detailLoading" class="block__dim">读取中…</text>
        <text v-else-if="detailError" class="block__err">{{ detailError }}</text>
        <text v-else-if="!detail?.ingredients.length" class="block__dim">
          这道菜还没录入食材信息
        </text>

        <view v-else class="ings">
          <view v-for="(ing, i) in detail.ingredients" :key="i" class="ing">
            <text class="ing__name">
              {{ ing.name }}<text v-if="ing.isCommonAllergen" class="ing__allergen">过敏原</text>
            </text>
            <text class="ing__amount">{{ ing.amount ?? '适量' }}</text>
          </view>
        </view>
      </view>

      <!-- 做法 -->
      <view class="block">
        <view class="block__head">
          <text class="block__mark">HOW</text>
          <text class="block__title">怎么做</text>
          <text v-if="detail?.recipe" class="block__badge">
            {{ detail.recipe.servings }} 人份 · {{ detail.recipe.cookMinutes }} 分钟 ·
            {{ detail.recipe.difficultyLabel }}
          </text>
        </view>

        <text v-if="detailLoading" class="block__dim">读取中…</text>

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

        <text v-else class="block__dim">
          这道菜还没有做法。去菜品详情页可以让 AI 一键生成。
        </text>
      </view>
    </view>

    <text v-if="phase === 'done'" class="congrats anim-pop">— 抽到了 —</text>
  </view>
</template>

<style lang="scss" scoped>
/* ═══════════════════════════════════════════════════════════
 *  布局原则：容器只管纵向排列，所有特效层用 absolute 且居中。
 *  上一版把光芒层设成 1100rpx 宽却只给了 top 没给 left，
 *  它的静态位置把整个页面撑歪了 —— 这是"样式崩了"的根因。
 * ═══════════════════════════════════════════════════════════ */
.reveal {
  position: relative;
  width: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  overflow: hidden;
  padding: $fm-gap-md 0 $fm-gap-lg;

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

/* ═══ 光芒 ═══════════════════════════════════════════════════ */
.rays {
  position: absolute;
  top: -80rpx;
  left: 50%;
  width: 900rpx;
  height: 900rpx;
  margin-left: -450rpx;
  pointer-events: none;
  opacity: 0.4;
  z-index: 0;

  &__spin {
    width: 100%;
    height: 100%;
    position: relative;
    animation: rays-spin 16s linear infinite;
  }

  &__line {
    position: absolute;
    left: 50%;
    top: 0;
    width: 3rpx;
    height: 450rpx;
    margin-left: -1.5rpx;
    transform-origin: 50% 100%;
    background: linear-gradient(180deg, transparent, var(--r));
    opacity: 0.4;
  }
}

@keyframes rays-spin {
  to {
    transform: rotate(360deg);
  }
}

/* ═══ 爆闪 ═══════════════════════════════════════════════════ */
.flash {
  position: absolute;
  top: 0;
  left: 50%;
  width: 900rpx;
  height: 900rpx;
  margin-left: -450rpx;
  border-radius: 50%;
  background: radial-gradient(circle, #fff 0%, rgba(255, 255, 255, 0.45) 32%, transparent 68%);
  animation: flash 0.7s ease-out both;
  pointer-events: none;
  z-index: 30;
}

@keyframes flash {
  0% {
    opacity: 0;
    transform: scale(0.6);
  }
  14% {
    opacity: 1;
    transform: scale(1);
  }
  100% {
    opacity: 0;
    transform: scale(1.25);
  }
}

/* ═══ 牌阵 ═══════════════════════════════════════════════════ */
.deck {
  width: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  z-index: 10;

  &__hint {
    font-family: $cy-mono;
    font-size: 26rpx;
    letter-spacing: 6rpx;
    color: $cy-cyan;
    text-shadow: 0 0 14rpx rgba(0, 240, 255, 0.9);
  }

  /* 横向一排，用 flex 而不是绝对定位 —— 真机上不会错位 */
  &__row {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 14rpx;
    margin: $fm-gap-lg 0;
    width: 100%;
  }

  &__sub {
    @include hud-label();
  }
}

/* ── 背面牌 ───────────────────────────────────────────────── */
.back {
  width: 116rpx;
  height: 180rpx;
  flex-shrink: 0;
  border-radius: $fm-radius-sm;
  background:
    repeating-linear-gradient(
      45deg,
      rgba(0, 240, 255, 0.09) 0px,
      rgba(0, 240, 255, 0.09) 6rpx,
      transparent 6rpx,
      transparent 14rpx
    ),
    linear-gradient(160deg, #12203a, #06060f);
  border: 1px solid rgba(0, 240, 255, 0.45);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10rpx;
  transition: all 0.22s ease;

  &__glyph {
    font-size: 40rpx;
    opacity: 0.8;
  }

  &__lines {
    display: flex;
    flex-direction: column;
    gap: 5rpx;
    width: 58%;
  }

  &__line {
    height: 2rpx;
    border-radius: 999rpx;
    background: rgba(0, 240, 255, 0.3);
  }

  /* 光标扫到这张 */
  &--cursor {
    transform: translateY(-14rpx) scale(1.1);
    border-color: $cy-cyan;
    box-shadow:
      0 0 8rpx rgba(0, 240, 255, 0.9),
      0 0 26rpx rgba(0, 240, 255, 0.55);
  }

  /* 落定：放大、加亮 */
  &--landed {
    transform: translateY(-26rpx) scale(1.24);
    border-color: var(--r);
    box-shadow:
      0 0 12rpx var(--r),
      0 0 44rpx var(--r);
    z-index: 20;
  }

  /* 其余牌：淡出 */
  &--out {
    opacity: 0.15;
    transform: scale(0.86);
  }
}

/* ═══ 稀有度横幅 ═════════════════════════════════════════════ */
.banner {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2rpx;
  padding: $fm-gap-md 0;
  z-index: 20;
  animation: banner-in 0.5s cubic-bezier(0.22, 1, 0.36, 1) both;

  &__stars {
    font-size: 32rpx;
    letter-spacing: 8rpx;
    color: var(--r);
    text-shadow: 0 0 18rpx var(--r);
  }

  &__key {
    font-family: $cy-mono;
    font-size: 64rpx;
    font-weight: 900;
    letter-spacing: 12rpx;
    color: var(--r);
    text-shadow:
      0 0 8rpx var(--r),
      0 0 30rpx var(--r);
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
    transform: translateY(-30rpx) scale(0.75);
  }
  60% {
    opacity: 1;
    transform: translateY(0) scale(1.06);
  }
  100% {
    transform: scale(1);
  }
}

/* ═══ 卡牌 ═══════════════════════════════════════════════════ */
.card {
  position: relative;
  width: 100%;
  max-width: 580rpx;
  padding: $fm-gap-lg;
  border-radius: $fm-radius-lg;
  background:
    linear-gradient(160deg, rgba(255, 255, 255, 0.06), transparent 42%),
    $cy-surface;
  border: 2px solid var(--r);
  box-shadow:
    0 0 8rpx var(--r),
    0 0 30rpx rgba(255, 255, 255, 0.12);
  display: flex;
  flex-direction: column;
  align-items: center;
  overflow: hidden;
  z-index: 15;
  animation: card-drop 0.7s cubic-bezier(0.22, 1, 0.36, 1) both;

  &--settled {
    animation: none;
  }

  &__shine {
    position: absolute;
    top: -40%;
    left: -60%;
    width: 45%;
    height: 180%;
    background: linear-gradient(100deg, transparent, rgba(255, 255, 255, 0.38), transparent);
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
    font-size: 52rpx;
    font-weight: 800;
    letter-spacing: 3rpx;
    text-align: center;
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
    font-size: 52rpx;
    font-weight: 900;
    color: var(--r);
    text-shadow: 0 0 18rpx var(--r);
  }

  &__unit {
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
    transform: translateY(90rpx) scale(0.6);
  }
  65% {
    opacity: 1;
    transform: translateY(0) scale(1.04);
  }
  100% {
    transform: translateY(0) scale(1);
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
}

.block {
  padding: $fm-gap-md $fm-gap-lg;
  background: $cy-surface;
  border: 1px solid rgba(0, 240, 255, 0.22);
  border-radius: $fm-radius-md;

  &__head {
    display: flex;
    align-items: baseline;
    gap: 12rpx;
    margin-bottom: $fm-gap-md;
    flex-wrap: wrap;
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

  &__text {
    font-size: 27rpx;
    line-height: 1.7;
    color: $cy-text-dim;
  }

  &__dim {
    font-family: $cy-mono;
    font-size: 22rpx;
    line-height: 1.7;
    color: $cy-text-faint;
  }

  &__err {
    font-family: $cy-mono;
    font-size: 22rpx;
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
  gap: $fm-gap-md;
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
    flex-shrink: 0;
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

/* ═══ 完成 ═══════════════════════════════════════════════════ */
.congrats {
  margin-top: $fm-gap-lg;
  font-family: $cy-mono;
  font-size: 24rpx;
  letter-spacing: 6rpx;
  color: var(--r);
  text-shadow: 0 0 14rpx var(--r);
}
</style>
