<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { dishApi, recordApi } from '@/api'
import type { DishBrief, RecordItem } from '@/api'
import { useMetaStore, useRecordStore } from '@/stores'

const meta = useMetaStore()
const records = useRecordStore()

const recordId = ref<string | null>(null)
const isEdit = computed(() => Boolean(recordId.value))
const saving = ref(false)
const loading = ref(false)

/* ── 表单状态 ── */
const selectedDish = ref<DishBrief | null>(null)
const dishName = ref('')
const mealType = ref(2)
const diningMode = ref(2)
const servings = ref(1)
const rating = ref(0)
const wouldEatAgain = ref<boolean | null>(null)
const note = ref('')
const eatenAt = ref<Date>(new Date())

/* ── 菜品搜索 ── */
const keyword = ref('')
const searchResults = ref<DishBrief[]>([])
const searching = ref(false)
const showSearch = ref(false)

const mealTypes = computed(() => meta.enums?.mealType ?? [
  { value: 1, label: '早餐' },
  { value: 2, label: '午餐' },
  { value: 3, label: '晚餐' },
  { value: 4, label: '夜宵' },
])

const diningModes = computed(() => meta.enums?.diningMode ?? [
  { value: 0, label: '随便' },
  { value: 1, label: '外卖' },
  { value: 2, label: '堂食' },
  { value: 3, label: '自己做' },
])

const servingOptions = [0.5, 1, 1.5, 2]

const dateText = computed(() => {
  const d = eatenAt.value
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
})

const canSave = computed(() => Boolean(selectedDish.value || dishName.value.trim()))

/* ── 搜索菜品 ── */
async function onSearch() {
  const k = keyword.value.trim()
  if (!k) {
    searchResults.value = []
    return
  }

  searching.value = true
  try {
    const page = await dishApi.list({ keyword: k, pageSize: 20 })
    searchResults.value = page.items
  } catch {
    searchResults.value = []
  } finally {
    searching.value = false
  }
}

function pickDish(dish: DishBrief) {
  selectedDish.value = dish
  dishName.value = dish.name
  showSearch.value = false
  keyword.value = ''
  searchResults.value = []
}

function clearDish() {
  selectedDish.value = null
  dishName.value = ''
}

/* ── 时间选择 ── */
function onDateChange(e: { detail: { value: string } }) {
  const next = new Date(e.detail.value)
  const current = eatenAt.value
  next.setHours(current.getHours(), current.getMinutes(), 0, 0)
  eatenAt.value = next
}

function onTimeChange(e: { detail: { value: string } }) {
  const [hour, minute] = e.detail.value.split(':').map(Number)
  const next = new Date(eatenAt.value)
  next.setHours(hour, minute, 0, 0)
  eatenAt.value = next
}

/* ── 保存 / 删除 ── */
async function onSave(force = false) {
  if (!canSave.value) {
    uni.showToast({ title: '请先选择或输入菜名', icon: 'none' })
    return
  }

  saving.value = true

  try {
    if (isEdit.value) {
      await records.update(recordId.value!, {
        mealType: mealType.value,
        diningMode: diningMode.value,
        servings: servings.value,
        eatenAt: eatenAt.value.toISOString(),
        note: note.value || undefined,
        ...(rating.value > 0 ? { rating: rating.value } : {}),
        ...(wouldEatAgain.value !== null ? { wouldEatAgain: wouldEatAgain.value } : {}),
      })

      await records.loadStats()
      uni.showToast({ title: '已保存', icon: 'none' })
      setTimeout(() => uni.navigateBack(), 600)
      return
    }

    const result = await records.create({
      dishId: selectedDish.value?.id ?? null,
      dishName: dishName.value.trim(),
      mealType: mealType.value,
      diningMode: diningMode.value,
      servings: servings.value,
      eatenAt: eatenAt.value.toISOString(),
      note: note.value || null,
      rating: rating.value > 0 ? rating.value : null,
      wouldEatAgain: wouldEatAgain.value,
      source: selectedDish.value ? 4 : 1,
      force,
    })

    if (!result.ok) {
      saving.value = false
      uni.showModal({
        title: '含忌口食材',
        content: `${result.conflict}\n\n记录是客观事实，仍要记下来吗？`,
        confirmText: '仍然记录',
        success: (res) => {
          if (res.confirm) void onSave(true)
        },
      })
      return
    }

    await records.loadStats()
    uni.showToast({ title: '已记录', icon: 'none' })
    setTimeout(() => uni.navigateBack(), 600)
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    saving.value = false
  }
}

async function onDelete() {
  if (!recordId.value) return

  const confirmed = await new Promise<boolean>((resolve) => {
    uni.showModal({
      title: '删除记录',
      content: '确定删除这条记录吗？',
      success: (res) => resolve(Boolean(res.confirm)),
      fail: () => resolve(false),
    })
  })

  if (!confirmed) return

  try {
    await records.remove(recordId.value)
    await records.loadStats()
    uni.showToast({ title: '已删除', icon: 'none' })
    setTimeout(() => uni.navigateBack(), 600)
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  }
}

onLoad(async (options) => {
  void meta.loadEnums()

  const id = options?.id as string | undefined
  if (!id) return

  recordId.value = id
  loading.value = true

  try {
    const record: RecordItem = await recordApi.detail(id)

    dishName.value = record.dishName
    mealType.value = record.mealType
    diningMode.value = record.diningMode
    servings.value = record.servings
    rating.value = record.rating ?? 0
    wouldEatAgain.value = record.wouldEatAgain
    note.value = record.note ?? ''
    eatenAt.value = new Date(record.eatenAt)

    if (record.dishId) {
      try {
        selectedDish.value = await dishApi.detail(record.dishId)
      } catch {
        // 菜品可能已下架，保留菜名即可
      }
    }
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <view class="page">
    <!-- 菜品 -->
    <view class="fm-card">
      <view class="section-head">
        <text class="section-title">吃了什么</text>
        <text class="link" @tap="showSearch = !showSearch">
          {{ showSearch ? '收起' : '从菜品库选' }}
        </text>
      </view>

      <view class="dish-picked">
        <input
          v-model="dishName"
          class="input"
          placeholder="输入菜名，如「番茄牛腩」"
          placeholder-class="input__placeholder"
          @input="selectedDish = null"
        />
        <text v-if="selectedDish" class="dish-picked__clear" @tap="clearDish">✕</text>
      </view>

      <text v-if="selectedDish" class="dish-picked__hint">
        已关联菜品库：{{ selectedDish.cuisineLabel }} · {{ selectedDish.spicyLabel }}
        <template v-if="selectedDish.calories"> · {{ selectedDish.calories }} kcal</template>
      </text>

      <!-- 搜索面板 -->
      <view v-if="showSearch" class="search">
        <view class="search__bar">
          <input
            v-model="keyword"
            class="input"
            placeholder="搜索菜品或别名"
            placeholder-class="input__placeholder"
            confirm-type="search"
            @confirm="onSearch"
          />
          <view class="search__button" @tap="onSearch">搜索</view>
        </view>

        <view v-if="searching" class="search__hint">搜索中…</view>
        <view v-else-if="keyword && !searchResults.length" class="search__hint">没有找到相关菜品</view>

        <view
          v-for="dish in searchResults"
          :key="dish.id"
          class="search__item"
          @tap="pickDish(dish)"
        >
          <text class="search__name">{{ dish.name }}</text>
          <text class="search__meta">
            {{ dish.cuisineLabel }} · {{ dish.categoryLabel }} · {{ dish.spicyLabel }}
          </text>
        </view>
      </view>
    </view>

    <!-- 餐次与方式 -->
    <view class="fm-card">
      <text class="section-title">哪一餐</text>
      <view class="fm-chips">
        <view
          v-for="m in mealTypes"
          :key="m.value"
          class="fm-chip"
          :class="{ 'fm-chip--active': mealType === m.value }"
          @tap="mealType = m.value"
        >
          {{ m.label }}
        </view>
      </view>

      <text class="section-title section-title--gap">怎么吃的</text>
      <view class="fm-chips">
        <view
          v-for="m in diningModes"
          :key="m.value"
          class="fm-chip"
          :class="{ 'fm-chip--active': diningMode === m.value }"
          @tap="diningMode = m.value"
        >
          {{ m.label }}
        </view>
      </view>
    </view>

    <!-- 时间与份量 -->
    <view class="fm-card">
      <text class="section-title">什么时候</text>
      <view class="row">
        <picker mode="date" :value="dateText.slice(0, 10)" @change="onDateChange">
          <view class="picker">{{ dateText.slice(0, 10) }}</view>
        </picker>
        <picker mode="time" :value="dateText.slice(11, 16)" @change="onTimeChange">
          <view class="picker">{{ dateText.slice(11, 16) }}</view>
        </picker>
      </view>

      <text class="section-title section-title--gap">份量</text>
      <view class="fm-chips">
        <view
          v-for="s in servingOptions"
          :key="s"
          class="fm-chip"
          :class="{ 'fm-chip--active': servings === s }"
          @tap="servings = s"
        >
          {{ s }} 份
        </view>
      </view>
    </view>

    <!-- 评分 -->
    <view class="fm-card">
      <view class="section-head">
        <text class="section-title">好吃吗</text>
        <text class="hint">可后补</text>
      </view>

      <view class="rating">
        <text
          v-for="n in 5"
          :key="n"
          class="rating__star"
          :class="{ 'rating__star--on': n <= rating }"
          @tap="rating = rating === n ? 0 : n"
        >★</text>
        <text class="rating__label">{{ rating > 0 ? `${rating} 分` : '还没评分' }}</text>
      </view>

      <view v-if="rating > 0" class="again">
        <text class="again__label">还想再吃吗</text>
        <view class="fm-chips">
          <view
            class="fm-chip"
            :class="{ 'fm-chip--active': wouldEatAgain === true }"
            @tap="wouldEatAgain = true"
          >想</view>
          <view
            class="fm-chip"
            :class="{ 'fm-chip--active': wouldEatAgain === false }"
            @tap="wouldEatAgain = false"
          >不想</view>
        </view>
      </view>

      <textarea
        v-model="note"
        class="textarea"
        placeholder="记点什么？比如「汤汁拌饭绝了」"
        placeholder-class="input__placeholder"
        :maxlength="200"
      />
    </view>

    <view
      class="fm-button"
      :class="{ 'fm-button--disabled': saving || !canSave }"
      @tap="onSave(false)"
    >
      {{ saving ? '保存中…' : isEdit ? '保存修改' : '记下这一顿' }}
    </view>

    <view v-if="isEdit" class="delete" @tap="onDelete">删除这条记录</view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

.section-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
}

.section-title {
  display: block;
  font-size: 30rpx;
  font-weight: 600;
  margin-bottom: $fm-gap-md;

  &--gap {
    margin-top: $fm-gap-lg;
  }
}

.hint {
  font-size: 22rpx;
  color: $fm-text-tertiary;
}

.link {
  font-size: 25rpx;
  color: $fm-primary;
}

.input {
  flex: 1;
  height: 76rpx;
  font-size: 28rpx;
  color: $fm-text;

  &__placeholder {
    color: $fm-text-tertiary;
  }
}

.dish-picked {
  display: flex;
  align-items: center;
  border-bottom: 2rpx solid $fm-border;

  &__clear {
    font-size: 30rpx;
    color: $fm-text-tertiary;
    padding: 0 12rpx;
  }

  &__hint {
    display: block;
    margin-top: 10rpx;
    font-size: 22rpx;
    color: $fm-primary;
  }
}

/* ── 搜索 ── */
.search {
  margin-top: $fm-gap-md;
  padding-top: $fm-gap-md;
  border-top: 2rpx solid $fm-border;

  &__bar {
    display: flex;
    align-items: center;
    gap: $fm-gap-sm;
    background: $fm-bg-muted;
    border-radius: $fm-radius-md;
    padding: 0 $fm-gap-md;
  }

  &__button {
    font-size: 26rpx;
    color: $fm-primary;
    font-weight: 600;
    padding: 12rpx 0 12rpx 20rpx;
  }

  &__hint {
    margin-top: $fm-gap-md;
    font-size: 24rpx;
    color: $fm-text-tertiary;
    text-align: center;
  }

  &__item {
    padding: $fm-gap-md 0;
    border-bottom: 2rpx solid $fm-border;

    &:last-child {
      border-bottom: none;
    }
  }

  &__name {
    font-size: 28rpx;
    font-weight: 600;
  }

  &__meta {
    display: block;
    margin-top: 4rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }
}

/* ── 时间 ── */
.row {
  display: flex;
  gap: $fm-gap-md;
}

.picker {
  padding: 18rpx 32rpx;
  background: $fm-bg-muted;
  border-radius: $fm-radius-md;
  font-size: 28rpx;
  color: $fm-text;
}

/* ── 评分 ── */
.rating {
  display: flex;
  align-items: center;
  gap: 10rpx;

  &__star {
    font-size: 56rpx;
    color: #d9dbe0;

    &--on {
      color: $fm-primary;
    }
  }

  &__label {
    margin-left: $fm-gap-md;
    font-size: 24rpx;
    color: $fm-text-tertiary;
  }
}

.again {
  margin-top: $fm-gap-md;

  &__label {
    display: block;
    font-size: 25rpx;
    color: $fm-text-secondary;
    margin-bottom: $fm-gap-sm;
  }
}

.textarea {
  width: 100%;
  height: 140rpx;
  margin-top: $fm-gap-lg;
  padding: $fm-gap-md;
  box-sizing: border-box;
  background: $fm-bg-muted;
  border-radius: $fm-radius-md;
  font-size: 26rpx;
}

.delete {
  margin-top: $fm-gap-lg;
  text-align: center;
  font-size: 27rpx;
  color: $fm-danger;
  padding: $fm-gap-md;
}
</style>
