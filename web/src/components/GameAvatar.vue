<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

/**
 * 美食伴侣的虚拟形象。
 *
 * 用几何图形拼出一个霓虹角色，而不是贴一张立绘 ——
 * 零素材、可无限缩放、表情能随状态实时变化。
 */
const props = withDefaults(
  defineProps<{
    /** 表情：影响眼睛与嘴的形状 */
    mood?: 'idle' | 'happy' | 'thinking' | 'excited'
    /** 是否正在说话（嘴部动画） */
    speaking?: boolean
    /** 直径（rpx） */
    size?: number
  }>(),
  { mood: 'idle', speaking: false, size: 180 },
)

/** 眨眼：随机间隔，让角色「活着」 */
const blinking = ref(false)
let blinkTimer: ReturnType<typeof setTimeout> | null = null

function scheduleBlink() {
  blinkTimer = setTimeout(() => {
    blinking.value = true
    setTimeout(() => {
      blinking.value = false
      scheduleBlink()
    }, 130)
  }, 2200 + Math.random() * 2600)
}

const accent = computed(() => {
  switch (props.mood) {
    case 'happy':
      return '#00FF9F'
    case 'excited':
      return '#FFC53D'
    case 'thinking':
      return '#A855F7'
    default:
      return '#00F0FF'
  }
})

/** 眼睛高度：眨眼时压扁，思考时眯起 */
const eyeHeight = computed(() => {
  if (blinking.value) return 8
  if (props.mood === 'thinking') return 16
  if (props.mood === 'happy') return 14
  return 26
})

/** 嘴：说话时上下张合，开心时上扬 */
const mouthClass = computed(() => {
  if (props.speaking) return 'mouth--talking'
  if (props.mood === 'happy' || props.mood === 'excited') return 'mouth--smile'
  return 'mouth--flat'
})

onMounted(scheduleBlink)
onUnmounted(() => {
  if (blinkTimer) clearTimeout(blinkTimer)
})
</script>

<template>
  <view
    class="avatar"
    :class="[`avatar--${mood}`, { 'avatar--speaking': speaking }]"
    :style="{ width: `${size}rpx`, height: `${size}rpx`, '--accent': accent }"
  >
    <!-- 外圈：双环旋转，像仪表盘的扫描环 -->
    <view class="avatar__ring avatar__ring--outer" />
    <view class="avatar__ring avatar__ring--inner" />

    <!-- 头 -->
    <view class="avatar__head">
      <view class="avatar__eyes">
        <view class="avatar__eye" :style="{ height: `${eyeHeight}rpx` }" />
        <view class="avatar__eye" :style="{ height: `${eyeHeight}rpx` }" />
      </view>
      <view class="avatar__mouth" :class="mouthClass" />
    </view>

    <!-- 底部光晕 -->
    <view class="avatar__glow" />
  </view>
</template>

<style lang="scss" scoped>
.avatar {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;

  /* ── 外圈 ─────────────────────────────────────── */
  &__ring {
    position: absolute;
    border-radius: 50%;
    border: 2px solid var(--accent);
    opacity: 0.5;

    &--outer {
      inset: 0;
      border-style: dashed;
      animation: avatar-spin 9s linear infinite;
    }

    &--inner {
      inset: 14%;
      opacity: 0.3;
      animation: avatar-spin 6s linear infinite reverse;
    }
  }

  /* ── 头 ───────────────────────────────────────── */
  &__head {
    position: relative;
    width: 62%;
    height: 62%;
    border-radius: 42%;
    background: radial-gradient(circle at 35% 30%, #14243a, #06060f 75%);
    border: 2px solid var(--accent);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10%;
    box-shadow:
      0 0 16rpx var(--accent),
      inset 0 0 30rpx rgba(0, 240, 255, 0.14);
    animation: avatar-float 3.4s ease-in-out infinite;
  }

  &__eyes {
    display: flex;
    gap: 22%;
  }

  &__eye {
    width: 14%;
    min-width: 10rpx;
    border-radius: 999rpx;
    background: var(--accent);
    box-shadow: 0 0 12rpx var(--accent);
    transition: height 0.1s ease;
  }

  /* ── 嘴 ───────────────────────────────────────── */
  &__mouth {
    width: 26%;
    height: 4rpx;
    border-radius: 999rpx;
    background: var(--accent);
    box-shadow: 0 0 10rpx var(--accent);
    transition: all 0.18s ease;

    &--flat {
      height: 4rpx;
    }

    &--smile {
      height: 10rpx;
      border-radius: 0 0 999rpx 999rpx;
    }

    &--talking {
      animation: avatar-talk 0.34s ease-in-out infinite;
    }
  }

  /* ── 底部光晕 ─────────────────────────────────── */
  &__glow {
    position: absolute;
    bottom: -6%;
    width: 60%;
    height: 10%;
    border-radius: 50%;
    background: var(--accent);
    filter: blur(10px);
    opacity: 0.4;
  }

  /* ── 状态 ─────────────────────────────────────── */
  &--excited &__ring--outer {
    animation-duration: 2.4s;
  }

  &--excited {
    animation: avatar-pop 0.7s cubic-bezier(0.22, 1, 0.36, 1);
  }

  &--thinking &__head {
    animation-duration: 5s;
  }
}

@keyframes avatar-spin {
  to {
    transform: rotate(360deg);
  }
}

@keyframes avatar-float {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-8rpx);
  }
}

@keyframes avatar-talk {
  0%,
  100% {
    height: 4rpx;
  }
  50% {
    height: 16rpx;
  }
}

@keyframes avatar-pop {
  0% {
    transform: scale(0.82);
  }
  55% {
    transform: scale(1.08);
  }
  100% {
    transform: scale(1);
  }
}
</style>
