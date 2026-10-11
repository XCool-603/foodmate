<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import GameAvatar from './GameAvatar.vue'

/**
 * 对话框。
 *
 * 打字机效果 + 选项按钮 —— 把「填表单」变成「和角色聊天」。
 * 点一下可以跳过打字动画直接出全文（游戏里的通用约定）。
 */
const props = withDefaults(
  defineProps<{
    /** 角色说的话 */
    text: string
    /** 角色名 */
    speaker?: string
    /** 可选项；为空表示这句话不需要选择 */
    choices?: { value: string | number; label: string }[]
    /** 角色表情 */
    mood?: 'idle' | 'happy' | 'thinking' | 'excited'
    /** 每个字的间隔（毫秒） */
    speed?: number
  }>(),
  { speaker: '小food', choices: () => [], mood: 'idle', speed: 45 },
)

const emit = defineEmits<{
  (e: 'choose', value: string | number): void
}>()

const shown = ref('')
const typing = ref(false)
let timer: ReturnType<typeof setTimeout> | null = null

function stop() {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
}

function type() {
  stop()
  shown.value = ''
  typing.value = true

  let i = 0
  const step = () => {
    if (i >= props.text.length) {
      typing.value = false
      return
    }

    shown.value += props.text[i]
    i++

    // 标点后稍作停顿，读起来更像说话
    const ch = props.text[i - 1]
    const pause = '，。！？、…'.includes(ch) ? props.speed * 5 : props.speed
    timer = setTimeout(step, pause)
  }

  step()
}

/** 点对话框：正在打字就立刻出全文，否则忽略 */
function onTapBox() {
  if (typing.value) {
    stop()
    shown.value = props.text
    typing.value = false
  }
}

/** 选项是否已就绪（文字打完才显示，避免抢跑） */
const choicesReady = computed(() => !typing.value && props.choices.length > 0)

watch(() => props.text, type, { immediate: true })
onUnmounted(stop)
</script>

<template>
  <view class="dlg">
    <!-- 角色 -->
    <view class="dlg__avatar">
      <GameAvatar :mood="mood" :speaking="typing" :size="150" />
      <text class="dlg__name">{{ speaker }}</text>
    </view>

    <!-- 气泡 -->
    <view class="dlg__body">
      <view class="dlg__bubble" hover-class="hover-dim" @tap="onTapBox">
        <text class="dlg__text">{{ shown }}</text>
        <text v-if="typing" class="dlg__caret">▌</text>
      </view>

      <!-- 选项 -->
      <view v-if="choicesReady" class="dlg__choices">
        <view
          v-for="(c, i) in choices"
          :key="c.value"
          class="dlg__choice anim-in"
          :style="{ animationDelay: `${i * 0.06}s` }"
          hover-class="hover-dim"
          @tap="emit('choose', c.value)"
        >
          <text class="dlg__choice-mark">▸</text>
          <text class="dlg__choice-text">{{ c.label }}</text>
        </view>
      </view>
    </view>
  </view>
</template>

<style lang="scss" scoped>
.dlg {
  display: flex;
  align-items: flex-start;
  gap: $fm-gap-md;

  &__avatar {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8rpx;
    flex-shrink: 0;
  }

  &__name {
    font-family: $cy-mono;
    font-size: 20rpx;
    letter-spacing: 2rpx;
    color: $cy-cyan;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.8);
  }

  &__body {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: $fm-gap-sm;
  }

  /* ── 气泡 ─────────────────────────────────────── */
  &__bubble {
    position: relative;
    padding: $fm-gap-md $fm-gap-lg;
    background:
      linear-gradient(150deg, rgba(0, 240, 255, 0.1), transparent 60%),
      $cy-surface;
    border: 1px solid rgba(0, 240, 255, 0.4);
    border-radius: $fm-radius-md;
    border-top-left-radius: 4rpx;
    box-shadow:
      0 0 14rpx rgba(0, 240, 255, 0.2),
      inset 0 0 30rpx rgba(0, 240, 255, 0.06);
    min-height: 96rpx;
  }

  &__text {
    font-size: 29rpx;
    line-height: 1.7;
    color: $cy-text;
  }

  &__caret {
    font-size: 26rpx;
    color: $cy-cyan;
    animation: fm-pulse 0.7s steps(1) infinite;
  }

  /* ── 选项 ─────────────────────────────────────── */
  &__choices {
    display: flex;
    flex-direction: column;
    gap: 10rpx;
  }

  &__choice {
    display: flex;
    align-items: center;
    gap: 10rpx;
    padding: 18rpx $fm-gap-lg;
    background: $cy-surface-2;
    border: 1px solid rgba(0, 240, 255, 0.3);
    border-radius: $fm-radius-md;
    transition: all 0.16s ease;

    &:active {
      background: rgba(0, 240, 255, 0.14);
      border-color: $cy-cyan;
      box-shadow: 0 0 14rpx rgba(0, 240, 255, 0.4);
    }
  }

  &__choice-mark {
    font-family: $cy-mono;
    font-size: 24rpx;
    color: $cy-cyan;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.8);
  }

  &__choice-text {
    flex: 1;
    font-size: 27rpx;
    color: $cy-text;
  }
}
</style>
