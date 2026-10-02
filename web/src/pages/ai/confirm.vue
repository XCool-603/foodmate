<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { dishApi } from '@/api'
import type { DishBrief } from '@/api'
import { useAiStore, useMetaStore, useRecordStore } from '@/stores'
import type { EditableItem } from '@/stores'

const ai = useAiStore()
const meta = useMetaStore()
const records = useRecordStore()

const items = ref<EditableItem[]>([])
const mealType = ref(2)
const diningMode = ref(2)
const saving = ref(false)

/** 正在为哪个条目搜索菜品库 */
const searchingIndex = ref<number | null>(null)
const keyword = ref('')
const searchResults = ref<DishBrief[]>([])
const searching = ref(false)

const mealTypes = computed(() => meta.enums?.mealType ?? [])
const diningModes = computed(() => meta.enums?.diningMode ?? [])

const conflictCount = computed(() => items.value.filter((i) => i.conflictIngredients.length).length)

const editedCount = computed(() => items.value.filter((i) => i.edited).length)

function onNameInput(item: EditableItem) {
  // 名字一改，与菜品库的关联就失效了——服务端会按新名字重新匹配或新建
  item.matchedDishId = null
  item.matchedDishName = null
  item.edited = true
}

function onPortionChange(item: EditableItem, delta: number) {
  const next = Math.round((item.portion + delta) * 2) / 2
  if (next >= 0.5 && next <= 5) {
    item.portion = next
    item.edited = true
  }
}

function onRemove(index: number) {
  items.value = items.value.filter((i) => i.index !== index)
}

async function onSearch(index: number) {
  searchingIndex.value = index
  keyword.value = items.value.find((i) => i.index === index)?.name ?? ''
  searchResults.value = []

  if (keyword.value) await doSearch()
}

async function doSearch() {
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
  const item = items.value.find((i) => i.index === searchingIndex.value)
  if (!item) return

  item.name = dish.name
  item.matchedDishId = dish.id
  item.matchedDishName = dish.name
  item.edited = true

  searchingIndex.value = null
  searchResults.value = []
  keyword.value = ''
}

async function onConfirm(force = false) {
  if (items.value.length === 0) {
    uni.showToast({ title: '至少保留一道菜', icon: 'none' })
    return
  }

  saving.value = true

  try {
    const result = await ai.confirm(items.value, mealType.value, diningMode.value, force)

    await records.loadStats()

    uni.showToast({
      title: `已记录 ${result.recordIds.length} 道菜`,
      icon: 'none',
    })

    ai.reset()
    setTimeout(() => uni.switchTab({ url: '/pages/records/index' }), 800)
  } catch (err) {
    const apiError = err as { code?: number; message?: string }

    if (apiError.code === 2003) {
      saving.value = false
      uni.showModal({
        title: '含忌口食材',
        content: `${apiError.message}\n\n仍要记录吗？`,
        confirmText: '仍然记录',
        success: (res) => {
          if (res.confirm) void onConfirm(true)
        },
      })
      return
    }

    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    saving.value = false
  }
}

onLoad(() => {
  void meta.loadEnums()

  if (!ai.hasResult) {
    uni.showToast({ title: '没有识别结果', icon: 'none' })
    setTimeout(() => uni.navigateBack(), 800)
    return
  }

  items.value = ai.editableItems.map((item) => ({ ...item }))

  // 识别出的场景直接用作默认就餐方式
  const scene = ai.recognition?.scene
  if (scene === 'dine_in') diningMode.value = 2
  else if (scene === 'takeout') diningMode.value = 1
  else if (scene === 'homemade') diningMode.value = 3
})
</script>

<template>
  <view class="page">
    <!-- 桩模型提示 -->
    <view v-if="ai.recognition?.isStubModel" class="banner banner--warn">
      当前是本地桩模型，识别结果与图片无关，仅供流程验证
    </view>

    <!-- 冲突提示 -->
    <view v-if="conflictCount" class="banner banner--danger">
      有 {{ conflictCount }} 道菜含你的忌口食材，已标红
    </view>

    <!-- 识别条目 -->
    <view class="fm-card">
      <view class="section-head">
        <text class="section-title">识别到 {{ items.length }} 道菜</text>
        <text class="hint">改动 {{ editedCount }} 处</text>
      </view>

      <view
        v-for="item in items"
        :key="item.index"
        class="item"
        :class="{
          'item--review': item.needsReview,
          'item--conflict': item.conflictIngredients.length,
        }"
      >
        <view class="item__head">
          <input
            :value="item.name"
            class="item__name"
            placeholder="菜名"
            @input="(e: any) => { item.name = e.detail.value; onNameInput(item) }"
          />
          <text class="item__remove" @tap="onRemove(item.index)">✕</text>
        </view>

        <view class="item__meta">
          <text class="item__tag">置信度 {{ item.confidence }}</text>
          <text v-if="item.needsReview" class="item__tag item__tag--warn">需复核</text>
          <text v-if="item.matchedDishId" class="item__tag item__tag--ok">
            已关联菜品库
          </text>
          <text v-else class="item__tag item__tag--new">将新建菜品</text>
          <text v-if="item.estimatedCalories" class="item__tag">
            约 {{ item.estimatedCalories }} kcal
          </text>
        </view>

        <text v-if="item.conflictIngredients.length" class="item__conflict">
          ⚠️ 含忌口食材：{{ item.conflictIngredients.join('、') }}
        </text>

        <view class="item__actions">
          <view class="portion">
            <text class="portion__button" @tap="onPortionChange(item, -0.5)">−</text>
            <text class="portion__value">{{ item.portion }} 份</text>
            <text class="portion__button" @tap="onPortionChange(item, 0.5)">＋</text>
          </view>
          <text class="link" @tap="onSearch(item.index)">从菜品库选</text>
        </view>

        <!-- 菜品库搜索 -->
        <view v-if="searchingIndex === item.index" class="search">
          <view class="search__bar">
            <input
              v-model="keyword"
              class="search__input"
              placeholder="搜索菜品"
              confirm-type="search"
              @confirm="doSearch"
            />
            <text class="search__button" @tap="doSearch">搜索</text>
          </view>

          <view v-if="searching" class="search__hint">搜索中…</view>
          <view v-else-if="!searchResults.length" class="search__hint">没有找到相关菜品</view>

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
    </view>

    <!-- 餐次与方式 -->
    <view class="fm-card">
      <text class="section-title">记到哪一餐</text>
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

    <view
      class="fm-button"
      :class="{ 'fm-button--disabled': saving || !items.length }"
      @tap="onConfirm(false)"
    >
      {{ saving ? '保存中…' : `确认并记录 ${items.length} 道菜` }}
    </view>

    <view class="footer">
      你的每一次修正都会被记录下来，用来让识别越来越准。
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

.banner {
  padding: $fm-gap-md $fm-gap-lg;
  border-radius: $fm-radius-md;
  font-size: 24rpx;
  margin-bottom: $fm-gap-md;
  line-height: 1.6;

  &--warn {
    background: rgba(245, 166, 35, 0.12);
    color: #a06800;
  }

  &--danger {
    background: rgba(229, 72, 77, 0.1);
    color: $fm-danger;
  }
}

.section-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: $fm-gap-md;
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

/* ── 条目 ── */
.item {
  padding: $fm-gap-md 0;
  border-bottom: 2rpx solid $fm-border;

  &:last-child {
    border-bottom: none;
  }

  &--review {
    background: rgba(245, 166, 35, 0.06);
    border-radius: $fm-radius-sm;
    padding-left: $fm-gap-md;
    padding-right: $fm-gap-md;
  }

  &--conflict {
    background: rgba(229, 72, 77, 0.07);
    border-radius: $fm-radius-sm;
    padding-left: $fm-gap-md;
    padding-right: $fm-gap-md;
  }

  &__head {
    display: flex;
    align-items: center;
  }

  &__name {
    flex: 1;
    height: 64rpx;
    font-size: 30rpx;
    font-weight: 600;
  }

  &__remove {
    font-size: 30rpx;
    color: $fm-text-tertiary;
    padding: 0 12rpx;
  }

  &__meta {
    display: flex;
    flex-wrap: wrap;
    gap: 10rpx;
    margin-top: 6rpx;
  }

  &__tag {
    font-size: 21rpx;
    color: $fm-text-tertiary;
    background: $fm-bg-muted;
    border-radius: $fm-radius-sm;
    padding: 4rpx 14rpx;

    &--warn {
      background: rgba(245, 166, 35, 0.18);
      color: #a06800;
    }

    &--ok {
      background: rgba(48, 163, 108, 0.12);
      color: $fm-success;
    }

    &--new {
      background: $fm-primary-soft;
      color: $fm-primary;
    }
  }

  &__conflict {
    display: block;
    margin-top: 10rpx;
    font-size: 23rpx;
    color: $fm-danger;
  }

  &__actions {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-top: $fm-gap-md;
  }
}

.portion {
  display: flex;
  align-items: center;
  gap: 16rpx;

  &__button {
    width: 52rpx;
    height: 52rpx;
    border-radius: 50%;
    background: $fm-bg-muted;
    color: $fm-text-secondary;
    font-size: 30rpx;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  &__value {
    font-size: 26rpx;
    min-width: 90rpx;
    text-align: center;
  }
}

.link {
  font-size: 25rpx;
  color: $fm-primary;
}

/* ── 搜索 ── */
.search {
  margin-top: $fm-gap-md;
  padding-top: $fm-gap-md;
  border-top: 2rpx solid $fm-border;

  &__bar {
    display: flex;
    align-items: center;
    background: $fm-bg-muted;
    border-radius: $fm-radius-md;
    padding: 0 $fm-gap-md;
  }

  &__input {
    flex: 1;
    height: 72rpx;
    font-size: 26rpx;
  }

  &__button {
    font-size: 26rpx;
    color: $fm-primary;
    font-weight: 600;
    padding-left: 20rpx;
  }

  &__hint {
    margin-top: $fm-gap-md;
    font-size: 23rpx;
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
    font-size: 27rpx;
    font-weight: 600;
  }

  &__meta {
    display: block;
    margin-top: 4rpx;
    font-size: 21rpx;
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
