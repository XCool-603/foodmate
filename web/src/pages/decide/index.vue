<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import DialogueBox from '@/components/DialogueBox.vue'
import { useAuthStore, useDecisionStore, useMetaStore } from '@/stores'
import { greeting } from '@/utils/format'

/**
 * 首页 = 一场对话。
 *
 * 不用表单，让角色一步步问 —— 每一次回答都会得到回应，
 * 最后交给结果页做抽卡演出。
 */
const meta = useMetaStore()
const decision = useDecisionStore()
const auth = useAuthStore()

type Step = 'greet' | 'mood' | 'budget' | 'party' | 'ready'

const step = ref<Step>('greet')
const busy = ref(false)

/* ── 对话收集到的答案 ── */
const moodLabel = ref('')
const moodTags = ref<string[]>([])
const budgetIndex = ref(1)
const partySize = ref(1)

const BUDGETS = [
  { label: '¥15 以内', min: 0, max: 1500, hint: '食堂、快餐' },
  { label: '¥15–30', min: 1500, max: 3000, hint: '家常小馆' },
  { label: '¥30–50', min: 3000, max: 5000, hint: '正经吃一顿' },
  { label: '¥50 以上', min: 5000, max: 15000, hint: '犒劳自己' },
]

/** 角色的台词与选项，按当前步骤给出 */
const scene = computed(() => {
  switch (step.value) {
    case 'greet':
      return {
        text: `${greeting()}。又到饭点了 —— 今天想吃点什么？`,
        mood: 'idle' as const,
        choices: [
          { value: 'any', label: '随便啦，你定' },
          { value: 'spicy', label: '想吃点辣的' },
          { value: 'light', label: '想吃清淡的' },
          { value: 'meat', label: '想吃肉' },
          { value: 'warm', label: '想喝点热的' },
        ],
      }

    case 'mood':
      return {
        text: `${moodLabel.value}，记下了。那预算大概多少？`,
        mood: 'happy' as const,
        choices: BUDGETS.map((b, i) => ({ value: i, label: `${b.label} · ${b.hint}` })),
      }

    case 'budget':
      return {
        text: '一个人吃，还是有人一起？',
        mood: 'idle' as const,
        choices: [
          { value: 1, label: '就我一个' },
          { value: 2, label: '两个人' },
          { value: 3, label: '三四个' },
          { value: 5, label: '一群人了' },
        ],
      }

    case 'party':
      return {
        text: '好，条件我都记住了。要我现在给你抽一张吗？',
        mood: 'excited' as const,
        choices: [{ value: 'draw', label: '抽！' }],
      }

    default:
      return {
        text: '正在翻牌…',
        mood: 'thinking' as const,
        choices: [],
      }
  }
})

const summary = computed(() => {
  const b = BUDGETS[budgetIndex.value]
  return `${moodLabel.value} · ${b.label} · ${partySize.value} 人`
})

/* ── 对话推进 ── */
async function onChoose(value: string | number) {
  if (busy.value) return

  switch (step.value) {
    case 'greet': {
      const map: Record<string, { label: string; tags: string[] }> = {
        any: { label: '随便都行', tags: [] },
        spicy: { label: '想吃辣的', tags: ['辣的'] },
        light: { label: '想吃清淡的', tags: ['清淡的'] },
        meat: { label: '想吃肉', tags: ['肉'] },
        warm: { label: '想喝热的', tags: ['暖胃的'] },
      }
      const picked = map[String(value)] ?? map.any
      moodLabel.value = picked.label
      moodTags.value = picked.tags
      step.value = 'mood'
      break
    }

    case 'mood':
      budgetIndex.value = Number(value)
      step.value = 'budget'
      break

    case 'budget':
      partySize.value = Number(value)
      step.value = 'party'
      break

    case 'party':
      await draw()
      break
  }
}

/** 调决策接口，然后跳结果页做抽卡演出 */
async function draw() {
  if (busy.value) return
  busy.value = true
  step.value = 'ready'

  try {
    const b = BUDGETS[budgetIndex.value]

    const response = await decision.suggest({
      mealType: null,
      diningMode: 0,
      partySize: partySize.value,
      budgetMinCents: b.min,
      budgetMaxCents: b.max,
      moodTags: [...moodTags.value],
      exploreJitter: 0,
    })

    if (response.ranked.length === 0) {
      uni.showModal({
        title: '没找到合适的',
        content: '试试放宽预算，或者去「我的 → 口味画像」看看是不是忌口设多了。',
        showCancel: false,
        success: () => {
          step.value = 'greet'
          busy.value = false
        },
      })
      return
    }

    uni.navigateTo({ url: '/pages/decide/result' })
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
    step.value = 'greet'
  } finally {
    busy.value = false
  }
}

function onRestart() {
  step.value = 'greet'
  moodLabel.value = ''
  moodTags.value = []
  budgetIndex.value = 1
  partySize.value = 1
}

onShow(() => {
  void meta.checkConnection()
  void meta.loadEnums()
  if (auth.isAuthenticated) void auth.loadMe()
})
</script>

<template>
  <view class="page">
    <!-- 顶部状态 -->
    <view class="sysbar">
      <text class="sysbar__dot" :class="`sysbar__dot--${meta.state}`">●</text>
      <text class="sysbar__text">
        {{ meta.isOnline ? `ONLINE · DISH ${meta.dishCount}` : 'OFFLINE' }}
      </text>
      <text v-if="step !== 'greet'" class="sysbar__reset" @tap="onRestart">重来</text>
    </view>

    <!-- 已收集的条件 -->
    <view v-if="step !== 'greet'" class="tags">
      <text class="tags__item">{{ summary }}</text>
    </view>

    <!-- 对话 -->
    <view class="stage">
      <DialogueBox
        :key="step"
        :text="scene.text"
        :choices="scene.choices"
        :mood="scene.mood"
        speaker="小food"
        @choose="onChoose"
      />
    </view>

    <!-- 离线提示 -->
    <view v-if="!meta.isOnline && meta.errorMessage" class="offline">
      <text class="offline__title">连不上后端</text>
      <text class="offline__desc">{{ meta.apiBaseUrl }}</text>
      <text class="offline__desc">{{ meta.errorMessage }}</text>
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  padding: $fm-gap-md;
  padding-bottom: calc(#{$fm-gap-lg} + env(safe-area-inset-bottom));
  box-sizing: border-box;
}

/* ── 顶部状态 ─────────────────────────────────────────────── */
.sysbar {
  display: flex;
  align-items: center;
  gap: 10rpx;
  padding: 0 4rpx $fm-gap-md;

  &__dot {
    font-size: 18rpx;

    &--online {
      color: $cy-lime;
      text-shadow: 0 0 12rpx rgba(0, 255, 159, 0.9);
    }

    &--offline {
      color: $cy-red;
      text-shadow: 0 0 12rpx rgba(255, 59, 92, 0.9);
    }

    &--checking,
    &--unknown {
      color: $cy-amber;
      animation: fm-pulse 1.2s ease-in-out infinite;
    }
  }

  &__text {
    @include hud-label();
  }

  &__reset {
    margin-left: auto;
    @include hud-label($cy-cyan);
  }
}

/* ── 已选条件 ─────────────────────────────────────────────── */
.tags {
  display: flex;
  flex-wrap: wrap;
  gap: 10rpx;
  margin-bottom: $fm-gap-md;

  &__item {
    padding: 8rpx 22rpx;
    border-radius: $fm-radius-pill;
    background: rgba(0, 240, 255, 0.1);
    border: 1px solid rgba(0, 240, 255, 0.4);
    font-family: $cy-mono;
    font-size: 21rpx;
    color: $cy-cyan;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.6);
  }
}

/* ── 对话区 ───────────────────────────────────────────────── */
.stage {
  flex: 1;
  padding-top: $fm-gap-lg;
}

/* ── 离线 ─────────────────────────────────────────────────── */
.offline {
  margin-top: $fm-gap-lg;
  padding: $fm-gap-md $fm-gap-lg;
  border-radius: $fm-radius-md;
  background: rgba(255, 59, 92, 0.08);
  border: 1px solid rgba(255, 59, 92, 0.4);

  &__title {
    display: block;
    font-size: 27rpx;
    font-weight: 700;
    color: $cy-red;
  }

  &__desc {
    display: block;
    margin-top: 6rpx;
    font-family: $cy-mono;
    font-size: 21rpx;
    color: $cy-text-faint;
    word-break: break-all;
  }
}
</style>
