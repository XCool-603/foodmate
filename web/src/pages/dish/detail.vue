<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { aiApi, dishApi } from '@/api'
import type { DishDetail, DishRecipe } from '@/api'
import { useRecordStore } from '@/stores'
import { formatPriceRange } from '@/utils/format'

const records = useRecordStore()

const dishId = ref<string | null>(null)
const dish = ref<DishDetail | null>(null)
const recipe = ref<DishRecipe | null>(null)

const loading = ref(true)
const generating = ref(false)
const error = ref<string | null>(null)

const hasConflict = computed(() => (dish.value?.conflictIngredients.length ?? 0) > 0)

const priceText = computed(() =>
  dish.value ? formatPriceRange(dish.value.priceMinCents, dish.value.priceMaxCents) : '—',
)

/** 菜谱总时长：优先用菜谱自己的，否则回落到菜品的 cookMinutes。 */
const totalMinutes = computed(() => recipe.value?.cookMinutes ?? 0)

async function load() {
  if (!dishId.value) return

  loading.value = true
  error.value = null

  try {
    const detail = await dishApi.detail(dishId.value)
    dish.value = detail
    recipe.value = detail.recipe
  } catch (err) {
    error.value = (err as Error).message
  } finally {
    loading.value = false
  }
}

/** 菜品库没缓存菜谱时，按需调用 AI 生成。 */
async function onGenerateRecipe() {
  if (!dish.value || generating.value) return

  generating.value = true
  error.value = null

  try {
    const generated = await aiApi.recipe(dish.value.id, dish.value.name, 2)

    recipe.value = {
      servings: generated.servings,
      cookMinutes: generated.cookMinutes,
      difficulty: generated.difficulty,
      difficultyLabel: generated.difficultyLabel,
      steps: generated.steps,
      tips: generated.tips,
    }

    if (generated.isStubModel) {
      uni.showToast({ title: '当前是本地桩模型，内容是模板', icon: 'none', duration: 2500 })
    }
  } catch (err) {
    error.value = (err as Error).message
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    generating.value = false
  }
}

function onRecord() {
  if (!dish.value) return

  uni.navigateTo({
    url: `/pages/records/edit?dishId=${dish.value.id}&dishName=${encodeURIComponent(dish.value.name)}`,
  })
}

function onCopyIngredients() {
  if (!dish.value) return

  const lines = dish.value.ingredients.map(
    (i) => `· ${i.name}${i.amount ? `  ${i.amount}` : ''}`,
  )

  uni.setClipboardData({
    data: `${dish.value.name}\n${lines.join('\n')}`,
    success: () => uni.showToast({ title: '食材已复制', icon: 'none' }),
  })
}

function onCopyRecipe() {
  if (!dish.value || !recipe.value) return

  const lines = [
    `【${dish.value.name}】${recipe.value.servings} 人份 · 约 ${recipe.value.cookMinutes} 分钟`,
    '',
    ...recipe.value.steps.map((s) => `${s.order}. ${s.text}`),
  ]

  if (recipe.value.tips) {
    lines.push('', `小贴士：${recipe.value.tips}`)
  }

  uni.setClipboardData({
    data: lines.join('\n'),
    success: () => uni.showToast({ title: '做法已复制', icon: 'none' }),
  })
}

onLoad((options) => {
  dishId.value = (options?.id as string) ?? null

  if (!dishId.value) {
    uni.showToast({ title: '缺少菜品 ID', icon: 'none' })
    setTimeout(() => uni.navigateBack(), 800)
    return
  }

  void load()
})
</script>

<template>
  <view class="page">
    <view v-if="loading" class="empty">加载中…</view>

    <view v-else-if="!dish" class="empty">
      <text class="empty__title">菜品不存在</text>
      <text class="empty__desc">{{ error }}</text>
    </view>

    <template v-else>
      <!-- 头部 -->
      <view class="hero">
        <text class="hero__name">{{ dish.name }}</text>
        <text v-if="dish.description" class="hero__desc">{{ dish.description }}</text>

        <view class="hero__meta">
          <text class="tag">{{ dish.cuisineLabel }}</text>
          <text class="tag">{{ dish.categoryLabel }}</text>
          <text class="tag">{{ dish.spicyLabel }}</text>
          <text class="tag">{{ priceText }}</text>
          <text v-if="dish.calories" class="tag">{{ dish.calories }} kcal</text>
        </view>

        <view v-if="dish.mealTimeLabels.length" class="hero__meals">
          适合：{{ dish.mealTimeLabels.join(' / ') }}
        </view>

        <view v-if="dish.tags.length" class="hero__tags">
          <text v-for="t in dish.tags" :key="t" class="chip">{{ t }}</text>
        </view>
      </view>

      <!-- 忌口冲突 -->
      <view v-if="hasConflict" class="banner banner--danger">
        ⚠️ 含你忌口的食材：{{ dish.conflictIngredients.join('、') }}
      </view>

      <!-- 食材 -->
      <view class="fm-card">
        <view class="section-head">
          <text class="section-title">需要什么</text>
          <text v-if="dish.ingredients.length" class="link" @tap="onCopyIngredients">复制清单</text>
        </view>

        <view v-if="!dish.ingredients.length" class="hint">
          这道菜还没录入食材信息。
        </view>

        <view v-else class="ingredients">
          <view
            v-for="(item, i) in dish.ingredients"
            :key="i"
            class="ingredient"
            :class="{ 'ingredient--conflict': dish.conflictIngredients.includes(item.name) }"
          >
            <text class="ingredient__name">
              {{ item.name }}
              <text v-if="item.isCommonAllergen" class="ingredient__allergen">过敏原</text>
            </text>
            <text class="ingredient__amount">{{ item.amount ?? '适量' }}</text>
          </view>
        </view>
      </view>

      <!-- 做法 -->
      <view class="fm-card">
        <view class="section-head">
          <text class="section-title">怎么做</text>
          <text v-if="recipe" class="link" @tap="onCopyRecipe">复制做法</text>
        </view>

        <!-- 还没有菜谱 -->
        <template v-if="!recipe">
          <text class="hint">
            菜品库里还没有这道菜的做法。可以让 AI 生成一份，也可以直接下厨自由发挥。
          </text>
          <view
            class="fm-button"
            :class="{ 'fm-button--disabled': generating }"
            @tap="onGenerateRecipe"
          >
            {{ generating ? '正在生成…' : '生成菜谱' }}
          </view>
        </template>

        <!-- 有菜谱 -->
        <template v-else>
          <view class="recipe-meta">
            <text class="recipe-meta__item">{{ recipe.servings }} 人份</text>
            <text class="recipe-meta__dot">·</text>
            <text class="recipe-meta__item">约 {{ totalMinutes }} 分钟</text>
            <text class="recipe-meta__dot">·</text>
            <text class="recipe-meta__item">{{ recipe.difficultyLabel }}</text>
          </view>

          <view class="steps">
            <view v-for="step in recipe.steps" :key="step.order" class="step">
              <view class="step__index">{{ step.order }}</view>
              <view class="step__body">
                <text class="step__text">{{ step.text }}</text>
                <text v-if="step.durationMinutes" class="step__time">
                  约 {{ step.durationMinutes }} 分钟
                </text>
              </view>
            </view>
          </view>

          <view v-if="recipe.tips" class="tips">
            <text class="tips__label">小贴士</text>
            <text class="tips__text">{{ recipe.tips }}</text>
          </view>
        </template>
      </view>

      <!-- 操作 -->
      <view class="fm-button" @tap="onRecord">记一顿</view>

      <view class="footer">
        食材与做法由 AI 生成时可能不准确，下厨前请自行判断。
      </view>
    </template>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 头部 ── */
.hero {
  padding: $fm-gap-md 8rpx $fm-gap-lg;

  &__name {
    display: block;
    font-size: 48rpx;
    font-weight: 700;
    line-height: 1.3;
  }

  &__desc {
    display: block;
    margin-top: 10rpx;
    font-size: 26rpx;
    color: $fm-text-secondary;
    line-height: 1.7;
  }

  &__meta {
    display: flex;
    flex-wrap: wrap;
    gap: 10rpx;
    margin-top: $fm-gap-md;
  }

  &__meals {
    margin-top: $fm-gap-sm;
    font-size: 23rpx;
    color: $fm-text-tertiary;
  }

  &__tags {
    display: flex;
    flex-wrap: wrap;
    gap: 10rpx;
    margin-top: $fm-gap-md;
  }
}

.tag {
  font-size: 22rpx;
  color: $fm-text-tertiary;
  background: $fm-bg-muted;
  border-radius: $fm-radius-sm;
  padding: 4rpx 14rpx;
}

.chip {
  font-family: $cy-mono;
  font-size: 20rpx;
  letter-spacing: 1rpx;
  color: $cy-cyan;
  background: rgba(0, 240, 255, 0.08);
  border: 1px solid rgba(0, 240, 255, 0.35);
  padding: 4rpx 16rpx;
}

.banner {
  padding: $fm-gap-md $fm-gap-lg;
  border-radius: $fm-radius-md;
  font-size: 24rpx;
  line-height: 1.6;
  margin-bottom: $fm-gap-md;

  &--danger {
    background: rgba(229, 72, 77, 0.1);
    color: $fm-danger;
  }
}

/* ── 小节 ── */
.section-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: $fm-gap-md;
}

.section-title {
  font-size: 30rpx;
  font-weight: 600;
}

.link {
  font-size: 25rpx;
  color: $fm-primary;
}

.hint {
  display: block;
  font-size: 24rpx;
  color: $fm-text-tertiary;
  line-height: 1.8;
  margin-bottom: $fm-gap-md;
}

/* ── 食材 ── */
.ingredients {
  display: flex;
  flex-direction: column;
}

.ingredient {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16rpx 0;
  border-bottom: 2rpx solid $fm-border;

  &:last-child {
    border-bottom: none;
  }

  &--conflict {
    background: rgba(229, 72, 77, 0.07);
    border-radius: $fm-radius-sm;
    padding-left: $fm-gap-md;
    padding-right: $fm-gap-md;
  }

  &__name {
    font-size: 27rpx;
  }

  &__allergen {
    margin-left: 10rpx;
    font-size: 20rpx;
    color: $fm-warning;
    background: rgba(245, 166, 35, 0.14);
    border-radius: $fm-radius-sm;
    padding: 2rpx 10rpx;
  }

  &__amount {
    font-size: 25rpx;
    color: $fm-text-tertiary;
  }
}

/* ── 菜谱 ── */
.recipe-meta {
  display: flex;
  align-items: center;
  gap: 10rpx;
  margin-bottom: $fm-gap-lg;

  &__item {
    font-size: 25rpx;
    color: $fm-text-secondary;
  }

  &__dot {
    color: $fm-text-tertiary;
  }
}

.steps {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-md;
}

.step {
  display: flex;
  gap: $fm-gap-md;

  &__index {
    width: 44rpx;
    height: 44rpx;
    background: rgba(0, 240, 255, 0.14);
    border: 1px solid $cy-cyan;
    color: $cy-cyan;
    font-family: $cy-mono;
    font-size: 24rpx;
    font-weight: 700;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    margin-top: 4rpx;
    box-shadow: 0 0 12rpx rgba(0, 240, 255, 0.3);
  }

  &__body {
    flex: 1;
    display: flex;
    flex-direction: column;
  }

  &__text {
    font-size: 27rpx;
    line-height: 1.7;
  }

  &__time {
    margin-top: 4rpx;
    font-size: 21rpx;
    color: $fm-text-tertiary;
  }
}

.tips {
  margin-top: $fm-gap-lg;
  padding: $fm-gap-md;
  background: $fm-primary-soft;
  border-radius: $fm-radius-md;

  &__label {
    display: block;
    font-size: 23rpx;
    color: $fm-primary;
    font-weight: 600;
    margin-bottom: 6rpx;
  }

  &__text {
    font-size: 25rpx;
    color: $fm-text-secondary;
    line-height: 1.7;
  }
}

/* ── 其它 ── */
.empty {
  padding: 100rpx 0;
  text-align: center;

  &__title {
    display: block;
    font-size: 30rpx;
    font-weight: 600;
  }

  &__desc {
    display: block;
    margin-top: 8rpx;
    font-size: 24rpx;
    color: $fm-text-tertiary;
  }
}

.footer {
  margin-top: $fm-gap-lg;
  text-align: center;
  font-size: 21rpx;
  color: $fm-text-tertiary;
  line-height: 1.7;
}
</style>
