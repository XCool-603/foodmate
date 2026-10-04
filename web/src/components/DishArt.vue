<script setup lang="ts">
import { computed } from 'vue'

/**
 * 菜品视觉。
 *
 * 有真实图片就显示图片；没有就**程序化生成一张「霓虹卡面」**：
 * 按菜系取色、按分类给图标、按辣度调光晕。
 *
 * 为什么不用占位图或图库：
 *   ① 20 道菜需要 20 张图，图库有版权问题，我也没法凭空生成照片
 *   ② 赛博朋克风格里，风格化图形本来就比照片更贴主题
 *      （赛博朋克 2077 的物品卡也是图形，不是照片）
 *   ③ 程序化生成零素材、离线可用，且每道菜的配色都与其菜系语义相关
 *
 * 想换成真实照片：把图片放进 `src/FoodMate.Api/wwwroot/images/dishes/`，
 * 在 `dishes.seed.json` 里给对应菜加 `"imageUrl": "/images/dishes/xxx.jpg"` 即可。
 */

const props = withDefaults(
  defineProps<{
    name: string
    /** 菜系枚举值 */
    cuisine: number
    /** 分类枚举值 */
    category: number
    /** 辣度 0–5 */
    spicyLevel?: number
    /** 真实图片地址；有则优先显示 */
    imageUrl?: string | null
    /** 形态 */
    variant?: 'thumb' | 'banner' | 'card'
    /** 是否叠加菜名水印（banner / card 默认开） */
    showName?: boolean
  }>(),
  { spicyLevel: 0, imageUrl: null, variant: 'thumb', showName: undefined },
)

/** 菜系 → 配色。冷色系菜系用青/蓝，辣味菜系用品红/橙红，日料/韩餐用紫。 */
const CUISINE_PALETTE: Record<number, { from: string; to: string; accent: string }> = {
  0: { from: '#1A1A2E', to: '#0B0B14', accent: '#00F0FF' }, // 家常
  1: { from: '#3A0A1E', to: '#12050C', accent: '#FF2E97' }, // 川菜
  2: { from: '#0A2E2A', to: '#05120F', accent: '#00FF9F' }, // 粤菜
  3: { from: '#3A1408', to: '#120705', accent: '#FF6B35' }, // 湘菜
  4: { from: '#2A1E08', to: '#0E0A04', accent: '#FFC53D' }, // 鲁菜
  5: { from: '#0A2438', to: '#050E17', accent: '#4DA6FF' }, // 苏菜
  6: { from: '#0A2A38', to: '#051017', accent: '#38D9E8' }, // 浙菜
  7: { from: '#1E0A38', to: '#0A0515', accent: '#A855F7' }, // 闽菜
  8: { from: '#2A2410', to: '#110E07', accent: '#D4A017' }, // 徽菜
  9: { from: '#102A38', to: '#071017', accent: '#5BC0EB' }, // 东北菜
  10: { from: '#38240A', to: '#170F05', accent: '#E8A33D' }, // 西北菜
  11: { from: '#0A3824', to: '#051710', accent: '#3DE88A' }, // 云贵菜
  12: { from: '#1A0A38', to: '#0A0515', accent: '#7C5CFF' }, // 日料
  13: { from: '#380A2A', to: '#170512', accent: '#FF4D8D' }, // 韩餐
  14: { from: '#2A1A0A', to: '#110B05', accent: '#FFB347' }, // 西餐
  15: { from: '#0A3818', to: '#05170B', accent: '#4DE88A' }, // 东南亚
  16: { from: '#28280A', to: '#111105', accent: '#E8E83D' }, // 快餐小吃
}

/** 分类 → 图标。 */
const CATEGORY_GLYPH: Record<number, string> = {
  0: '🍽',
  1: '🍚',
  2: '🍖',
  3: '🥬',
  4: '🍲',
  5: '🍢',
  6: '🍰',
  7: '🥤',
  8: '🥐',
  9: '🔥',
}

const palette = computed(() => CUISINE_PALETTE[props.cuisine] ?? CUISINE_PALETTE[0])
const glyph = computed(() => CATEGORY_GLYPH[props.category] ?? '🍽')

/** 名字首字，作为水印。 */
const initial = computed(() => props.name.trim().charAt(0) || '食')

/** 辣度越高，光晕越强。 */
const heat = computed(() => Math.min(1, props.spicyLevel / 5))

const showWatermark = computed(() =>
  props.showName ?? props.variant !== 'thumb',
)

/** 真实图片优先。 */
const hasImage = computed(() => Boolean(props.imageUrl))
</script>

<template>
  <view
    class="art"
    :class="[`art--${variant}`]"
    :style="{
      '--from': palette.from,
      '--to': palette.to,
      '--accent': palette.accent,
      '--heat': heat,
    }"
  >
    <!-- 真实图片 -->
    <image v-if="hasImage" class="art__image" :src="imageUrl!" mode="aspectFill" />

    <!-- 程序化卡面 -->
    <template v-else>
      <view class="art__base" />
      <view class="art__stripes" />

      <!-- 装饰圆环 -->
      <view class="art__ring art__ring--outer" />
      <view class="art__ring art__ring--inner" />

      <!-- 首字水印 -->
      <text v-if="showWatermark" class="art__watermark">{{ initial }}</text>

      <!-- 主图标 -->
      <text class="art__glyph">{{ glyph }}</text>

      <!-- 辣度光晕 -->
      <view v-if="heat > 0.4" class="art__heat" />
    </template>
  </view>
</template>

<style lang="scss" scoped>
.art {
  position: relative;
  overflow: hidden;
  background: var(--to);
  flex-shrink: 0;

  &--thumb {
    width: 128rpx;
    height: 128rpx;
    border-radius: $fm-radius-md;
  }

  &--banner {
    width: 100%;
    height: 300rpx;
    border-radius: $fm-radius-lg;
  }

  &--card {
    width: 100%;
    height: 220rpx;
    border-radius: $fm-radius-md;
  }

  &__image {
    width: 100%;
    height: 100%;
    display: block;
  }

  /* ── 底色渐变 ─────────────────────────────────── */
  &__base {
    position: absolute;
    inset: 0;
    background: linear-gradient(145deg, var(--from) 0%, var(--to) 70%);
  }

  /* ── 斜纹 ─────────────────────────────────────── */
  &__stripes {
    position: absolute;
    inset: 0;
    background: repeating-linear-gradient(
      45deg,
      rgba(255, 255, 255, 0.035) 0px,
      rgba(255, 255, 255, 0.035) 6rpx,
      transparent 6rpx,
      transparent 16rpx
    );
  }

  /* ── 装饰圆环 ─────────────────────────────────── */
  &__ring {
    position: absolute;
    border-radius: 50%;
    border: 1px solid var(--accent);
    opacity: 0.35;
    pointer-events: none;

    &--outer {
      width: 260rpx;
      height: 260rpx;
      top: -110rpx;
      right: -90rpx;
      opacity: 0.25;
    }

    &--inner {
      width: 160rpx;
      height: 160rpx;
      top: -60rpx;
      right: -40rpx;
      opacity: 0.4;
    }
  }

  /* ── 首字水印 ─────────────────────────────────── */
  &__watermark {
    position: absolute;
    right: 16rpx;
    bottom: -30rpx;
    font-size: 200rpx;
    font-weight: 800;
    line-height: 1;
    color: var(--accent);
    opacity: 0.1;
    pointer-events: none;
  }

  /* ── 主图标 ───────────────────────────────────── */
  &__glyph {
    position: absolute;
    left: 50%;
    top: 50%;
    transform: translate(-50%, -50%);
    font-size: 96rpx;
    line-height: 1;
    filter: drop-shadow(0 0 16rpx var(--accent));
  }

  /* ── 辣度光晕 ─────────────────────────────────── */
  &__heat {
    position: absolute;
    inset: 0;
    background: radial-gradient(
      circle at 30% 80%,
      rgba(255, 59, 92, calc(0.45 * var(--heat))) 0%,
      transparent 60%
    );
    pointer-events: none;
  }

  /* ── 尺寸微调 ─────────────────────────────────── */
  &--thumb &__glyph {
    font-size: 56rpx;
    filter: drop-shadow(0 0 10rpx var(--accent));
  }

  &--thumb &__ring,
  &--thumb &__watermark {
    display: none;
  }

  &--card &__glyph {
    font-size: 72rpx;
  }

  &--banner &__glyph {
    font-size: 120rpx;
  }

  &--banner &__watermark {
    font-size: 260rpx;
    right: 30rpx;
    bottom: -50rpx;
  }
}
</style>
