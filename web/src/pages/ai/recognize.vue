<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { useAiStore, useMetaStore } from '@/stores'

const ai = useAiStore()
const meta = useMetaStore()

const picked = ref(false)

const busy = computed(() => ai.uploading || ai.recognizing)

const statusText = computed(() => {
  if (ai.uploading) return '正在上传图片…'
  if (ai.recognizing) return '正在辨认菜品…'
  return ''
})

async function onPick() {
  picked.value = true
  await ai.pickAndRecognize()

  if (ai.hasResult) {
    uni.navigateTo({ url: '/pages/ai/confirm' })
  }
}

/** 识别失败时的兜底出口——绝不能因为 AI 挂了就让用户记不了。 */
function onManual() {
  uni.redirectTo({ url: '/pages/records/edit' })
}

function onGoConfirm() {
  uni.navigateTo({ url: '/pages/ai/confirm' })
}

function onRetry() {
  ai.reset()
  picked.value = false
}

onShow(() => {
  void meta.loadEnums()
})
</script>

<template>
  <view class="page">
    <!-- 拍照区 -->
    <view class="capture" :class="{ 'capture--busy': busy }" @tap="busy ? null : onPick()">
      <text class="capture__icon">{{ busy ? '⏳' : '📷' }}</text>
      <text class="capture__title">{{ busy ? statusText : '拍一张，自动记录' }}</text>
      <text v-if="!busy" class="capture__hint">对着饭菜拍一张，我来认认是什么</text>
      <text v-else class="capture__hint">首次识别通常需要 3–5 秒</text>
    </view>

    <!-- 桩模型提示 -->
    <view v-if="ai.recognition?.isStubModel" class="notice notice--warn">
      <text class="notice__title">⚠️ 当前使用本地桩模型</text>
      <text class="notice__body">
        识别结果<b>与图片内容无关</b>，只是为了让整条流程能离线跑通。
        接入真实视觉模型请在 <text class="mono">appsettings.json</text> 里把
        <text class="mono">Ai:Provider</text> 改为 <text class="mono">OpenAiCompatible</text> 并配置密钥。
      </text>
    </view>

    <!-- 错误与降级 -->
    <view v-if="ai.error" class="notice notice--error">
      <text class="notice__title">识别没成功</text>
      <text class="notice__body">{{ ai.error }}</text>
      <view class="notice__actions">
        <view class="notice__button" @tap="onRetry">重试</view>
        <view class="notice__button notice__button--primary" @tap="onManual">手动记录</view>
      </view>
    </view>

    <!-- 上次结果 -->
    <view v-if="picked && !busy && ai.hasResult" class="notice">
      <text class="notice__title">识别到 {{ ai.recognition?.items.length }} 道菜</text>
      <text class="notice__body">
        场景：{{ ai.recognition?.sceneLabel }} ·
        整体置信度 {{ ai.recognition?.overallConfidence }}
        <template v-if="ai.reviewCount"> · {{ ai.reviewCount }} 道需要你确认</template>
      </text>
      <view class="notice__actions">
        <view class="notice__button notice__button--primary" @tap="onGoConfirm">
          去确认
        </view>
      </view>
    </view>

    <!-- 说明 -->
    <view class="fm-card tips">
      <text class="tips__title">为什么需要你确认</text>
      <text class="tips__line">
        视觉模型会认错菜、也会把份量估歪。直接入库会污染你的饮食记录，
        所以识别结果一定先给你过目。
      </text>
      <text class="tips__line">
        你的每一次修正都会被记录下来，用来让识别越来越准。
      </text>
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
}

/* ── 拍照区 ── */
.capture {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 420rpx;
  border-radius: $fm-radius-lg;
  background: $fm-bg-card;
  border: 4rpx dashed $fm-border;
  margin-bottom: $fm-gap-md;

  &--busy {
    border-color: $fm-primary;
    background: $fm-primary-soft;
  }

  &__icon {
    font-size: 96rpx;
    line-height: 1;
  }

  &__title {
    margin-top: $fm-gap-md;
    font-size: 34rpx;
    font-weight: 700;
  }

  &__hint {
    margin-top: 10rpx;
    font-size: 24rpx;
    color: $fm-text-tertiary;
  }
}

/* ── 提示条 ── */
.notice {
  background: $fm-bg-card;
  border-radius: $fm-radius-md;
  padding: $fm-gap-lg;
  margin-bottom: $fm-gap-md;

  &--warn {
    background: rgba(245, 166, 35, 0.1);
  }

  &--error {
    background: rgba(229, 72, 77, 0.08);
  }

  &__title {
    display: block;
    font-size: 28rpx;
    font-weight: 600;
  }

  &__body {
    display: block;
    margin-top: 8rpx;
    font-size: 24rpx;
    color: $fm-text-secondary;
    line-height: 1.7;
  }

  &__actions {
    display: flex;
    gap: $fm-gap-md;
    margin-top: $fm-gap-md;
  }

  &__button {
    flex: 1;
    text-align: center;
    padding: 20rpx;
    background: $cy-surface-2;
    border: 1px solid $cy-line;
    font-family: $cy-mono;
    font-size: 25rpx;
    color: $cy-text-dim;

    &--primary {
      background: rgba(0, 240, 255, 0.14);
      border: 1px solid $cy-cyan;
      color: $cy-cyan;
      font-weight: 700;
      box-shadow: 0 0 14rpx rgba(0, 240, 255, 0.3);
    }
  }
}

.mono {
  font-family: 'SF Mono', Consolas, Monaco, monospace;
  font-size: 22rpx;
  color: $fm-primary;
}

/* ── 说明 ── */
.tips {
  &__title {
    display: block;
    font-size: 28rpx;
    font-weight: 600;
    margin-bottom: $fm-gap-sm;
  }

  &__line {
    display: block;
    font-size: 24rpx;
    color: $fm-text-tertiary;
    line-height: 1.8;

    & + & {
      margin-top: 8rpx;
    }
  }
}
</style>
