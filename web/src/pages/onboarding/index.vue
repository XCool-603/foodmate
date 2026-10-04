<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { profileApi } from '@/api'
import { useAuthStore, useMetaStore } from '@/stores'

/**
 * 新用户引导。
 *
 * 全程用标签块而非滑块 —— 引导页的目标是「30 秒内拿到初始画像」，
 * 不是让用户精细调节。要细调去「口味画像」页。
 */
const meta = useMetaStore()
const auth = useAuthStore()

const STEPS = 5
const step = ref(1)
const submitting = ref(false)

/* ── 答案 ── */
const spicyLevel = ref(2)
const budgetIndex = ref(1)
const avoidIngredients = ref<string[]>([])
const preferredCuisines = ref<number[]>([])
const diningMode = ref(1)
const customAvoid = ref('')

/** 预算档位。用区间中值作为画像的上下限。 */
const BUDGET_PRESETS = [
  { label: '¥15 以内', min: 0, max: 1500, hint: '食堂 / 快餐' },
  { label: '¥15–30', min: 1500, max: 3000, hint: '家常小馆' },
  { label: '¥30–50', min: 3000, max: 5000, hint: '正经吃一顿' },
  { label: '¥50 以上', min: 5000, max: 15000, hint: '犒劳自己' },
]

const DINING_MODES = [
  { value: 1, label: '外卖', glyph: '🛵' },
  { value: 2, label: '堂食', glyph: '🍽' },
  { value: 3, label: '自己做', glyph: '🍳' },
]

const spicyOptions = computed(() => meta.enums?.spicyLevel ?? [])
const cuisineOptions = computed(() => meta.enums?.cuisine ?? [])
const allergenOptions = computed(() => meta.enums?.avoidIngredient ?? [])

const budget = computed(() => BUDGET_PRESETS[budgetIndex.value])

const canNext = computed(() => {
  // 只有忌口那一步允许为空（没忌口是常态），其余步骤都有默认值
  return true
})

/* ── 交互 ── */
function next() {
  if (step.value < STEPS) {
    step.value += 1
    return
  }

  void submit()
}

function prev() {
  if (step.value > 1) step.value -= 1
}

function toggleAvoid(item: string) {
  const i = avoidIngredients.value.indexOf(item)
  if (i >= 0) avoidIngredients.value.splice(i, 1)
  else avoidIngredients.value.push(item)
}

function addCustomAvoid() {
  const word = customAvoid.value.trim()
  if (!word) return

  if (word.length > 10) {
    uni.showToast({ title: '单个词不能超过 10 个字', icon: 'none' })
    return
  }

  if (!avoidIngredients.value.includes(word)) avoidIngredients.value.push(word)
  customAvoid.value = ''
}

function toggleCuisine(value: number) {
  const i = preferredCuisines.value.indexOf(value)
  if (i >= 0) preferredCuisines.value.splice(i, 1)
  else preferredCuisines.value.push(value)
}

async function submit() {
  if (submitting.value) return
  submitting.value = true

  try {
    await profileApi.completeOnboarding({
      spicyLevel: spicyLevel.value,
      budgetMinCents: budget.value.min,
      budgetMaxCents: budget.value.max,
      avoidIngredients: [...avoidIngredients.value],
      preferredCuisines: [...preferredCuisines.value],
      diningMode: diningMode.value,
    })

    auth.markOnboardingCompleted()

    uni.showToast({ title: '画像已就绪', icon: 'none' })

    setTimeout(() => {
      uni.switchTab({ url: '/pages/decide/index' })
    }, 700)
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    submitting.value = false
  }
}

function skip() {
  uni.showModal({
    title: '跳过引导',
    content: '跳过会使用默认画像（微辣、¥15–50、无忌口）。之后可以在「我的 → 口味画像」里随时调整。',
    confirmText: '跳过',
    cancelText: '继续填',
    success: (res) => {
      if (res.confirm) uni.switchTab({ url: '/pages/decide/index' })
    },
  })
}

onLoad(() => {
  void meta.loadEnums()
})
</script>

<template>
  <view class="page">
    <!-- ── 进度 ───────────────────────────────────────── -->
    <view class="progress">
      <view class="progress__dots">
        <view
          v-for="i in STEPS"
          :key="i"
          class="progress__dot"
          :class="{
            'progress__dot--done': i < step,
            'progress__dot--active': i === step,
          }"
        />
      </view>
      <text class="progress__count">{{ step }} / {{ STEPS }}</text>
    </view>

    <!-- ── 步骤内容 ───────────────────────────────────── -->
    <view class="stage">
      <!-- 1. 辣度 -->
      <view v-if="step === 1" :key="1" class="step anim-in">
        <text class="step__no">01</text>
        <text class="step__q">你能吃多辣？</text>
        <text class="step__hint">
          选「不辣」后，中辣及以上的菜会几乎被排除在推荐之外
        </text>
        <view class="fm-chips step__chips">
          <view
            v-for="s in spicyOptions"
            :key="s.value"
            class="fm-chip"
            :class="{ 'fm-chip--active': spicyLevel === s.value }"
            hover-class="hover-dim"
            @tap="spicyLevel = s.value"
          >
            {{ s.label }}
          </view>
        </view>
      </view>

      <!-- 2. 预算 -->
      <view v-else-if="step === 2" :key="2" class="step anim-in">
        <text class="step__no">02</text>
        <text class="step__q">一顿饭大概花多少？</text>
        <text class="step__hint">超预算的菜会被扣分，但不会完全消失</text>
        <view class="budget-list anim-stagger">
          <view
            v-for="(b, i) in BUDGET_PRESETS"
            :key="i"
            class="budget"
            :class="{ 'budget--active': budgetIndex === i }"
            hover-class="hover-dim"
            @tap="budgetIndex = i"
          >
            <view class="budget__main">
              <text class="budget__label">{{ b.label }}</text>
              <text class="budget__hint">{{ b.hint }}</text>
            </view>
            <text v-if="budgetIndex === i" class="budget__check">✓</text>
          </view>
        </view>
      </view>

      <!-- 3. 忌口 -->
      <view v-else-if="step === 3" :key="3" class="step anim-in">
        <text class="step__no">03</text>
        <text class="step__q">有什么忌口或过敏？</text>
        <text class="step__hint">
          这里勾选的食材，含它的菜品<b>完全不会出现在推荐里</b>——不是分数低，是直接排除
        </text>

        <view class="fm-chips step__chips">
          <view
            v-for="item in allergenOptions"
            :key="item"
            class="fm-chip"
            :class="{ 'fm-chip--active': avoidIngredients.includes(item) }"
            hover-class="hover-dim"
            @tap="toggleAvoid(item)"
          >
            {{ item }}
          </view>
        </view>

        <view v-if="avoidIngredients.filter((x) => !allergenOptions.includes(x)).length" class="customs">
          <view
            v-for="item in avoidIngredients.filter((x) => !allergenOptions.includes(x))"
            :key="item"
            class="custom"
            hover-class="hover-dim"
            @tap="toggleAvoid(item)"
          >
            {{ item }} ✕
          </view>
        </view>

        <view class="custom-input">
          <input
            v-model="customAvoid"
            class="custom-input__field"
            placeholder="其他忌口，如「香菜」"
            placeholder-class="ph"
            confirm-type="done"
            @confirm="addCustomAvoid"
          />
          <text class="custom-input__btn" @tap="addCustomAvoid">添加</text>
        </view>

        <text class="step__skip">没有忌口？直接下一步</text>
      </view>

      <!-- 4. 菜系 -->
      <view v-else-if="step === 4" :key="4" class="step anim-in">
        <text class="step__no">04</text>
        <text class="step__q">爱吃哪些菜系？</text>
        <text class="step__hint">可多选，也可以一个都不选（不选就不偏向任何菜系）</text>
        <view class="fm-chips step__chips">
          <view
            v-for="c in cuisineOptions"
            :key="c.value"
            class="fm-chip"
            :class="{ 'fm-chip--active': preferredCuisines.includes(c.value) }"
            hover-class="hover-dim"
            @tap="toggleCuisine(c.value)"
          >
            {{ c.label }}
          </view>
        </view>
      </view>

      <!-- 5. 就餐方式 -->
      <view v-else :key="5" class="step anim-in">
        <text class="step__no">05</text>
        <text class="step__q">平时怎么吃得多？</text>
        <text class="step__hint">决定推荐时更偏向哪一类，之后可以随时改</text>
        <view class="modes anim-stagger">
          <view
            v-for="m in DINING_MODES"
            :key="m.value"
            class="mode"
            :class="{ 'mode--active': diningMode === m.value }"
            hover-class="hover-dim"
            @tap="diningMode = m.value"
          >
            <text class="mode__glyph">{{ m.glyph }}</text>
            <text class="mode__label">{{ m.label }}</text>
          </view>
        </view>
      </view>
    </view>

    <!-- ── 操作 ───────────────────────────────────────── -->
    <view class="actions">
      <view
        v-if="step > 1"
        class="fm-button fm-button--ghost actions__prev"
        hover-class="hover-dim"
        @tap="prev"
      >
        上一步
      </view>
      <view
        class="fm-button actions__next"
        :class="{ 'fm-button--disabled': submitting }"
        hover-class="hover-dim"
        @tap="next"
      >
        {{ step === STEPS ? (submitting ? '保存中…' : '开始使用') : '下一步' }}
      </view>
    </view>

    <text class="skip" @tap="skip">跳过引导</text>
  </view>
</template>

<style lang="scss" scoped>
.page {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  padding: $fm-gap-lg $fm-gap-md calc(#{$fm-gap-lg} + env(safe-area-inset-bottom));
  box-sizing: border-box;
}

/* ── 进度 ─────────────────────────────────────────────────── */
.progress {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 8rpx $fm-gap-lg;

  &__dots {
    display: flex;
    gap: 12rpx;
  }

  &__dot {
    width: 44rpx;
    height: 6rpx;
    border-radius: $fm-radius-pill;
    background: $cy-surface-2;
    transition: all 0.32s cubic-bezier(0.22, 1, 0.36, 1);

    &--done {
      background: rgba(0, 240, 255, 0.45);
    }

    &--active {
      width: 72rpx;
      background: $cy-cyan;
      box-shadow: 0 0 14rpx rgba(0, 240, 255, 0.7);
    }
  }

  &__count {
    font-family: $cy-mono;
    font-size: 22rpx;
    letter-spacing: 2rpx;
    color: $cy-text-faint;
  }
}

/* ── 步骤 ─────────────────────────────────────────────────── */
.stage {
  flex: 1;
}

.step {
  display: flex;
  flex-direction: column;

  &__no {
    font-family: $cy-mono;
    font-size: 22rpx;
    font-weight: 700;
    letter-spacing: 3rpx;
    color: $cy-cyan;
    text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.6);
  }

  &__q {
    margin-top: 12rpx;
    font-size: 46rpx;
    font-weight: 800;
    line-height: 1.35;
    letter-spacing: 1rpx;
  }

  &__hint {
    margin-top: 14rpx;
    font-size: 24rpx;
    line-height: 1.7;
    color: $cy-text-faint;
  }

  &__chips {
    margin-top: $fm-gap-lg;
  }

  &__skip {
    margin-top: $fm-gap-md;
    font-family: $cy-mono;
    font-size: 21rpx;
    color: $cy-text-faint;
  }
}

/* ── 预算 ─────────────────────────────────────────────────── */
.budget-list {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-sm;
  margin-top: $fm-gap-lg;
}

.budget {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: $fm-gap-md $fm-gap-lg;
  background: $cy-surface;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;
  transition: all 0.2s ease;

  &--active {
    border-color: $cy-cyan;
    background: rgba(0, 240, 255, 0.07);
    box-shadow: 0 0 16rpx rgba(0, 240, 255, 0.25);
  }

  &__main {
    display: flex;
    flex-direction: column;
    gap: 4rpx;
  }

  &__label {
    font-size: 30rpx;
    font-weight: 700;
  }

  &__hint {
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
  }

  &__check {
    font-size: 30rpx;
    color: $cy-cyan;
    text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.7);
  }
}

/* ── 自定义忌口 ───────────────────────────────────────────── */
.customs {
  display: flex;
  flex-wrap: wrap;
  gap: $fm-gap-sm;
  margin-top: $fm-gap-md;
}

.custom {
  padding: 10rpx 24rpx;
  font-family: $cy-mono;
  font-size: 23rpx;
  color: $cy-red;
  background: rgba(255, 59, 92, 0.1);
  border: 1px solid rgba(255, 59, 92, 0.4);
  border-radius: $fm-radius-pill;
}

.custom-input {
  display: flex;
  align-items: center;
  gap: $fm-gap-sm;
  margin-top: $fm-gap-md;
  padding: 0 $fm-gap-md;
  background: $cy-surface-2;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;

  &__field {
    flex: 1;
    height: 80rpx;
    font-size: 26rpx;
    color: $cy-text;
  }

  &__btn {
    font-family: $cy-mono;
    font-size: 25rpx;
    color: $cy-cyan;
    font-weight: 700;
    padding: 12rpx 0 12rpx 20rpx;
  }
}

.ph {
  color: $cy-text-faint;
}

/* ── 就餐方式 ─────────────────────────────────────────────── */
.modes {
  display: flex;
  gap: $fm-gap-md;
  margin-top: $fm-gap-lg;
}

.mode {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12rpx;
  padding: $fm-gap-lg 0;
  background: $cy-surface;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;
  transition: all 0.2s ease;

  &--active {
    border-color: $cy-cyan;
    background: rgba(0, 240, 255, 0.07);
    box-shadow: 0 0 18rpx rgba(0, 240, 255, 0.3);
  }

  &__glyph {
    font-size: 56rpx;
    line-height: 1;
  }

  &__label {
    font-size: 27rpx;
    font-weight: 600;
  }
}

/* ── 操作 ─────────────────────────────────────────────────── */
.actions {
  display: flex;
  gap: $fm-gap-md;
  margin-top: $fm-gap-lg;

  &__prev {
    flex: 0 0 200rpx;
  }

  &__next {
    flex: 1;
  }
}

.skip {
  margin-top: $fm-gap-md;
  text-align: center;
  font-family: $cy-mono;
  font-size: 22rpx;
  letter-spacing: 1rpx;
  color: $cy-text-faint;
}
</style>
