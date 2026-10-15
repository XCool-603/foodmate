<script setup lang="ts">
import { computed, ref } from 'vue'
import { onReachBottom, onShow } from '@dcloudio/uni-app'
import { dishApi } from '@/api'
import type { DishBrief } from '@/api'
import DishArt from '@/components/DishArt.vue'
import SkeletonBlock from '@/components/SkeletonBlock.vue'
import { useMetaStore } from '@/stores'
import { formatPriceRange } from '@/utils/format'

/**
 * 菜品库。
 *
 * 设计文档 4.1 的页面地图里规划了这一页，但一直没建 ——
 * 结果是 301 道菜只能在首页看到 6 道，「浏览」这条路径是断的。
 */
const meta = useMetaStore()

const keyword = ref('')
const cuisine = ref<number | null>(null)
const category = ref<number | null>(null)

const items = ref<DishBrief[]>([])
const total = ref(0)
const page = ref(1)
const loading = ref(false)
const finished = ref(false)
const firstLoad = ref(true)

const PAGE_SIZE = 30

const cuisines = computed(() => meta.enums?.cuisine ?? [])
const categories = computed(() => meta.enums?.dishCategory ?? [])

const hasFilter = computed(
  () => Boolean(keyword.value.trim()) || cuisine.value !== null || category.value !== null,
)

const filterSummary = computed(() => {
  const parts: string[] = []
  if (keyword.value.trim()) parts.push(`「${keyword.value.trim()}」`)
  if (cuisine.value !== null) {
    parts.push(cuisines.value.find((c) => c.value === cuisine.value)?.label ?? '')
  }
  if (category.value !== null) {
    parts.push(categories.value.find((c) => c.value === category.value)?.label ?? '')
  }
  return parts.filter(Boolean).join(' · ')
})

async function load(reset = false) {
  if (loading.value) return
  if (reset) {
    page.value = 1
    finished.value = false
    items.value = []
  }
  if (finished.value) return

  loading.value = true

  try {
    const result = await dishApi.list({
      keyword: keyword.value.trim() || undefined,
      cuisine: cuisine.value ?? undefined,
      category: category.value ?? undefined,
      page: page.value,
      pageSize: PAGE_SIZE,
    })

    total.value = result.total
    items.value = reset ? result.items : [...items.value, ...result.items]

    if (items.value.length >= result.total || result.items.length === 0) {
      finished.value = true
    } else {
      page.value += 1
    }
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
    finished.value = true
  } finally {
    loading.value = false
    firstLoad.value = false
  }
}

/** 搜索框确认 */
function onSearch() {
  void load(true)
}

function onClearKeyword() {
  keyword.value = ''
  void load(true)
}

function onPickCuisine(value: number | null) {
  cuisine.value = cuisine.value === value ? null : value
  void load(true)
}

function onPickCategory(value: number | null) {
  category.value = category.value === value ? null : value
  void load(true)
}

function onReset() {
  keyword.value = ''
  cuisine.value = null
  category.value = null
  void load(true)
}

function onOpenDish(dish: DishBrief) {
  uni.navigateTo({ url: `/pages/dish/detail?id=${dish.id}` })
}

/** 一键记一顿 */
function onQuickRecord(dish: DishBrief) {
  uni.navigateTo({
    url: `/pages/records/edit?dishId=${dish.id}&dishName=${encodeURIComponent(dish.name)}`,
  })
}

onShow(() => {
  void meta.loadEnums()
  if (items.value.length === 0) void load(true)
})

onReachBottom(() => {
  void load()
})
</script>

<template>
  <view class="page">
    <!-- ── 搜索 ───────────────────────────────────────── -->
    <view class="search">
      <text class="search__icon">🔍</text>
      <input
        v-model="keyword"
        class="search__input"
        placeholder="搜菜名，如「番茄」「面」"
        placeholder-class="ph"
        confirm-type="search"
        @confirm="onSearch"
      />
      <text v-if="keyword" class="search__clear" @tap="onClearKeyword">✕</text>
    </view>

    <!-- ── 筛选 ───────────────────────────────────────── -->
    <scroll-view class="filters" scroll-x :show-scrollbar="false">
      <view class="filters__row">
        <view
          class="chip"
          :class="{ 'chip--on': cuisine === null && category === null }"
          @tap="onReset"
        >
          全部
        </view>

        <view
          v-for="c in cuisines"
          :key="`cu-${c.value}`"
          class="chip"
          :class="{ 'chip--on': cuisine === c.value }"
          @tap="onPickCuisine(c.value)"
        >
          {{ c.label }}
        </view>
      </view>
    </scroll-view>

    <scroll-view class="filters" scroll-x :show-scrollbar="false">
      <view class="filters__row">
        <view
          v-for="c in categories"
          :key="`cat-${c.value}`"
          class="chip chip--cat"
          :class="{ 'chip--on': category === c.value }"
          @tap="onPickCategory(c.value)"
        >
          {{ c.label }}
        </view>
      </view>
    </scroll-view>

    <!-- ── 结果统计 ───────────────────────────────────── -->
    <view class="meta">
      <text class="meta__count">
        共 <text class="meta__num">{{ total }}</text> 道
      </text>
      <text v-if="hasFilter" class="meta__filter">{{ filterSummary }}</text>
      <text v-if="hasFilter" class="meta__reset" @tap="onReset">清除</text>
    </view>

    <!-- ── 列表 ───────────────────────────────────────── -->
    <SkeletonBlock v-if="firstLoad && loading" :rows="3" avatar />

    <view v-else-if="items.length === 0" class="empty">
      <text class="empty__title">没找到符合条件的菜</text>
      <text class="empty__desc">换个关键词，或清除筛选条件</text>
    </view>

    <view v-else class="list anim-stagger">
      <view
        v-for="dish in items"
        :key="dish.id"
        class="dish"
        hover-class="hover-lift"
        @tap="onOpenDish(dish)"
      >
        <DishArt
          variant="thumb"
          :name="dish.name"
          :cuisine="dish.cuisine"
          :category="dish.category"
          :spicy-level="dish.spicyLevel"
          :image-url="dish.imageUrl"
        />

        <view class="dish__body">
          <view class="dish__head">
            <text class="dish__name">{{ dish.name }}</text>
            <text class="dish__price">
              {{ formatPriceRange(dish.priceMinCents, dish.priceMaxCents) }}
            </text>
          </view>

          <view class="dish__tags">
            <text class="tag">{{ dish.cuisineLabel }}</text>
            <text class="tag">{{ dish.categoryLabel }}</text>
            <text class="tag">{{ dish.spicyLabel }}</text>
            <text v-if="dish.calories" class="tag">{{ dish.calories }} kcal</text>
            <text v-if="dish.hasRecipe" class="tag tag--recipe">有做法</text>
          </view>

          <view class="dish__actions">
            <text class="link" @tap.stop="onQuickRecord(dish)">记一顿</text>
            <text class="link link--primary">看详情 ›</text>
          </view>
        </view>
      </view>
    </view>

    <!-- ── 底部 ───────────────────────────────────────── -->
    <view v-if="items.length > 0" class="foot">
      <text v-if="loading" class="foot__text">加载中…</text>
      <text v-else-if="finished" class="foot__text">— 到底了 · 共 {{ total }} 道 —</text>
      <text v-else class="foot__text">上拉加载更多</text>
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 搜索 ─────────────────────────────────────────────────── */
.search {
  display: flex;
  align-items: center;
  gap: 12rpx;
  padding: 0 $fm-gap-md;
  height: 84rpx;
  background: $cy-surface-2;
  border: 1px solid rgba(0, 240, 255, 0.3);
  border-radius: $fm-radius-md;

  &__icon {
    font-size: 26rpx;
    opacity: 0.7;
  }

  &__input {
    flex: 1;
    height: 100%;
    font-size: 27rpx;
    color: $cy-text;
  }

  &__clear {
    font-size: 26rpx;
    color: $cy-text-faint;
    padding: 10rpx;
  }
}

.ph {
  color: $cy-text-faint;
}

/* ── 筛选 ─────────────────────────────────────────────────── */
.filters {
  margin-top: $fm-gap-md;
  white-space: nowrap;
  width: 100%;

  &__row {
    display: inline-flex;
    gap: 10rpx;
    padding-right: $fm-gap-md;
  }
}

.chip {
  flex-shrink: 0;
  padding: 10rpx 24rpx;
  border-radius: $fm-radius-pill;
  background: $cy-surface-2;
  border: 1px solid rgba(0, 240, 255, 0.2);
  font-family: $cy-mono;
  font-size: 23rpx;
  color: $cy-text-dim;
  transition: all 0.16s ease;

  &--cat {
    font-size: 22rpx;
  }

  &--on {
    background: rgba(0, 240, 255, 0.16);
    border-color: $cy-cyan;
    color: $cy-cyan;
    font-weight: 700;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.8);
    box-shadow: 0 0 14rpx rgba(0, 240, 255, 0.4);
  }
}

/* ── 统计 ─────────────────────────────────────────────────── */
.meta {
  display: flex;
  align-items: center;
  gap: 12rpx;
  margin: $fm-gap-md 4rpx;

  &__count {
    font-family: $cy-mono;
    font-size: 22rpx;
    color: $cy-text-faint;
  }

  &__num {
    color: $cy-cyan;
    font-weight: 700;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.7);
  }

  &__filter {
    flex: 1;
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
  }

  &__reset {
    font-family: $cy-mono;
    font-size: 21rpx;
    color: $cy-cyan;
  }
}

/* ── 列表 ─────────────────────────────────────────────────── */
.list {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-sm;
}

.dish {
  display: flex;
  gap: $fm-gap-md;
  padding: $fm-gap-md;
  background: $cy-surface;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;

  &__body {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }

  &__head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: $fm-gap-sm;
  }

  &__name {
    font-size: 30rpx;
    font-weight: 700;
    letter-spacing: 1rpx;
  }

  &__price {
    font-family: $cy-mono;
    font-size: 24rpx;
    color: $cy-amber;
    flex-shrink: 0;
  }

  &__tags {
    display: flex;
    flex-wrap: wrap;
    gap: 8rpx;
    margin-top: 10rpx;
  }

  &__actions {
    display: flex;
    justify-content: space-between;
    margin-top: $fm-gap-md;
  }
}

.tag {
  font-family: $cy-mono;
  font-size: 19rpx;
  color: $cy-text-faint;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-sm;
  padding: 2rpx 10rpx;

  &--recipe {
    color: $cy-lime;
    border-color: rgba(0, 255, 159, 0.4);
    background: rgba(0, 255, 159, 0.08);
  }
}

.link {
  font-family: $cy-mono;
  font-size: 22rpx;
  color: $cy-text-faint;

  &--primary {
    color: $cy-cyan;
    font-weight: 700;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.5);
  }
}

/* ── 空 / 底 ──────────────────────────────────────────────── */
.empty {
  padding: 120rpx 0;
  text-align: center;

  &__title {
    display: block;
    font-size: 29rpx;
    font-weight: 600;
  }

  &__desc {
    display: block;
    margin-top: 8rpx;
    font-family: $cy-mono;
    font-size: 22rpx;
    color: $cy-text-faint;
  }
}

.foot {
  padding: $fm-gap-lg 0;
  text-align: center;

  &__text {
    @include hud-label();
  }
}
</style>
