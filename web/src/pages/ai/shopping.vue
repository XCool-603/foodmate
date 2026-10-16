<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad, onShow } from '@dcloudio/uni-app'
import { aiApi, dishApi } from '@/api'
import type { DishBrief, ShoppingList } from '@/api'
import DishArt from '@/components/DishArt.vue'
import { useDecisionStore, useMetaStore } from '@/stores'

/**
 * 买菜清单。
 *
 * 设计文档的功能清单里有「买菜清单生成」（P1，M3），后端接口也早就实现了，
 * 但前端一直没有页面 —— 这是「自己做」闭环缺的最后一环。
 *
 * 流程：选菜 → 定份数 → 生成 → 按类分组 → 复制带走
 */
const meta = useMetaStore()
const decision = useDecisionStore()

const selected = ref<DishBrief[]>([])
const servings = ref(2)
const list = ref<ShoppingList | null>(null)
const generating = ref(false)
const error = ref<string | null>(null)

/** 候选：搜索出来的菜 */
const keyword = ref('')
const candidates = ref<DishBrief[]>([])
const searching = ref(false)

const selectedIds = computed(() => new Set(selected.value.map((d) => d.id)))

const canGenerate = computed(() => selected.value.length > 0 && !generating.value)

/** 按 category 分组展示 */
const grouped = computed(() => {
  const items = list.value?.items ?? []
  const map = new Map<string, typeof items>()

  for (const item of items) {
    const key = item.category?.trim() || '其他'
    const bucket = map.get(key) ?? []
    bucket.push(item)
    map.set(key, bucket)
  }

  return [...map.entries()].map(([category, entries]) => ({ category, entries }))
})

const SERVINGS = [1, 2, 3, 4, 6]

async function search() {
  searching.value = true
  try {
    const r = await dishApi.list({
      keyword: keyword.value.trim() || undefined,
      pageSize: 20,
    })
    candidates.value = r.items
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  } finally {
    searching.value = false
  }
}

function toggle(dish: DishBrief) {
  if (selectedIds.value.has(dish.id)) {
    selected.value = selected.value.filter((d) => d.id !== dish.id)
  } else {
    if (selected.value.length >= 8) {
      uni.showToast({ title: '一次最多 8 道菜', icon: 'none' })
      return
    }
    selected.value = [...selected.value, dish]
  }
  // 改了选菜就作废旧清单，避免看到过期结果
  list.value = null
}

function removeSelected(id: string) {
  selected.value = selected.value.filter((d) => d.id !== id)
  list.value = null
}

async function generate() {
  if (!canGenerate.value) return

  generating.value = true
  error.value = null

  try {
    list.value = await aiApi.shoppingList(
      selected.value.map((d) => d.id),
      servings.value,
    )

    if (list.value.isStubModel) {
      uni.showToast({
        title: '当前是本地桩模型，清单为模板内容',
        icon: 'none',
        duration: 2500,
      })
    }
  } catch (err) {
    error.value = (err as Error).message
  } finally {
    generating.value = false
  }
}

function onCopy() {
  if (!list.value) return

  const lines: string[] = [`买菜清单 · ${servings.value} 人份`, '']

  for (const g of grouped.value) {
    lines.push(`【${g.category}】`)
    for (const item of g.entries) {
      lines.push(`· ${item.name}${item.totalAmount ? `  ${item.totalAmount}` : ''}`)
    }
    lines.push('')
  }

  uni.setClipboardData({
    data: lines.join('\n'),
    success: () => uni.showToast({ title: '清单已复制', icon: 'none' }),
  })
}

function onClear() {
  selected.value = []
  list.value = null
  error.value = null
}

/** 从决策结果一键带入 */
function loadFromDecision() {
  const picks = decision.wheelItems.slice(0, 4)
  if (picks.length === 0) {
    uni.showToast({ title: '还没有推荐结果', icon: 'none' })
    return
  }

  selected.value = picks.map((p) => p.dish)
  list.value = null
  uni.showToast({ title: `已带入 ${picks.length} 道`, icon: 'none' })
}

onLoad(() => {
  void meta.loadEnums()
  void search()
})

onShow(() => {
  if (candidates.value.length === 0) void search()
})
</script>

<template>
  <view class="page">
    <!-- ── 已选 ───────────────────────────────────────── -->
    <view class="picked">
      <view class="picked__head">
        <text class="picked__title">已选 {{ selected.length }} 道</text>
        <text v-if="selected.length" class="picked__clear" @tap="onClear">清空</text>
        <text v-else class="picked__from" @tap="loadFromDecision">带入推荐</text>
      </view>

      <view v-if="!selected.length" class="picked__empty">
        从下面挑几道菜，我来给你算要买什么
      </view>

      <view v-else class="picked__chips">
        <view
          v-for="d in selected"
          :key="d.id"
          class="picked__chip"
          hover-class="hover-dim"
          @tap="removeSelected(d.id)"
        >
          {{ d.name }} ✕
        </view>
      </view>
    </view>

    <!-- ── 份数 ───────────────────────────────────────── -->
    <view class="row">
      <text class="row__label">SERVINGS</text>
      <view class="row__chips">
        <view
          v-for="s in SERVINGS"
          :key="s"
          class="chip"
          :class="{ 'chip--on': servings === s }"
          hover-class="hover-dim"
          @tap="servings = s"
        >
          {{ s }} 人份
        </view>
      </view>
    </view>

    <!-- ── 生成 ───────────────────────────────────────── -->
    <view
      class="fm-button"
      :class="{ 'fm-button--disabled': !canGenerate }"
      hover-class="hover-dim"
      @tap="generate"
    >
      {{ generating ? '正在算…' : '生成买菜清单' }}
    </view>

    <!-- ── 清单结果 ───────────────────────────────────── -->
    <view v-if="list" class="result anim-in">
      <view class="result__head">
        <text class="result__mark">LIST</text>
        <text class="result__title">要买这些</text>
        <text class="result__copy" @tap="onCopy">复制</text>
      </view>

      <view v-for="g in grouped" :key="g.category" class="group">
        <text class="group__name">{{ g.category }}</text>
        <view class="group__items">
          <view v-for="(item, i) in g.entries" :key="i" class="item">
            <view class="item__main">
              <text class="item__name">{{ item.name }}</text>
              <text v-if="item.forDishes.length" class="item__for">
                用于 {{ item.forDishes.join('、') }}
              </text>
            </view>
            <text class="item__amount">{{ item.totalAmount ?? '适量' }}</text>
          </view>
        </view>
      </view>

      <view v-if="list.isStubModel" class="stub">
        当前是本地桩模型，清单是按食材累加的模板内容。接入真实文本模型后会按菜品智能合并。
      </view>
    </view>

    <view v-if="error" class="err">{{ error }}</view>

    <!-- ── 搜索加菜 ───────────────────────────────────── -->
    <view class="search">
      <text class="search__icon">🔍</text>
      <input
        v-model="keyword"
        class="search__input"
        placeholder="搜菜名加进来"
        placeholder-class="ph"
        confirm-type="search"
        @confirm="search"
      />
    </view>

    <view v-if="searching" class="dim">搜索中…</view>

    <view v-else class="cands anim-stagger">
      <view
        v-for="d in candidates"
        :key="d.id"
        class="cand"
        :class="{ 'cand--on': selectedIds.has(d.id) }"
        hover-class="hover-dim"
        @tap="toggle(d)"
      >
        <DishArt
          variant="thumb"
          :name="d.name"
          :cuisine="d.cuisine"
          :category="d.category"
          :spicy-level="d.spicyLevel"
          :image-url="d.imageUrl"
        />
        <view class="cand__body">
          <text class="cand__name">{{ d.name }}</text>
          <text class="cand__meta">{{ d.cuisineLabel }} · {{ d.categoryLabel }}</text>
        </view>
        <text class="cand__check">{{ selectedIds.has(d.id) ? '✓' : '+' }}</text>
      </view>
    </view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 60rpx;
}

/* ── 已选 ─────────────────────────────────────────────────── */
.picked {
  padding: $fm-gap-md $fm-gap-lg;
  background: $cy-surface;
  border: 1px solid rgba(0, 240, 255, 0.25);
  border-radius: $fm-radius-md;

  &__head {
    display: flex;
    align-items: baseline;
    gap: 12rpx;
  }

  &__title {
    font-size: 28rpx;
    font-weight: 700;
  }

  &__clear,
  &__from {
    margin-left: auto;
    font-family: $cy-mono;
    font-size: 21rpx;
    color: $cy-cyan;
  }

  &__empty {
    margin-top: 10rpx;
    font-size: 24rpx;
    color: $cy-text-faint;
  }

  &__chips {
    display: flex;
    flex-wrap: wrap;
    gap: 10rpx;
    margin-top: $fm-gap-md;
  }

  &__chip {
    padding: 8rpx 22rpx;
    border-radius: $fm-radius-pill;
    background: rgba(0, 240, 255, 0.12);
    border: 1px solid rgba(0, 240, 255, 0.45);
    font-family: $cy-mono;
    font-size: 22rpx;
    color: $cy-cyan;
  }
}

/* ── 份数 ─────────────────────────────────────────────────── */
.row {
  display: flex;
  align-items: center;
  gap: $fm-gap-md;
  margin: $fm-gap-md 0;

  &__label {
    @include hud-label();
  }

  &__chips {
    display: flex;
    gap: 10rpx;
    flex-wrap: wrap;
  }
}

.chip {
  padding: 8rpx 20rpx;
  border-radius: $fm-radius-pill;
  background: $cy-surface-2;
  border: 1px solid rgba(0, 240, 255, 0.2);
  font-family: $cy-mono;
  font-size: 22rpx;
  color: $cy-text-dim;

  &--on {
    background: rgba(0, 240, 255, 0.16);
    border-color: $cy-cyan;
    color: $cy-cyan;
    font-weight: 700;
    box-shadow: 0 0 14rpx rgba(0, 240, 255, 0.4);
  }
}

/* ── 结果 ─────────────────────────────────────────────────── */
.result {
  margin-top: $fm-gap-lg;
  padding: $fm-gap-md $fm-gap-lg;
  background: $cy-surface;
  border: 1px solid rgba(0, 240, 255, 0.25);
  border-radius: $fm-radius-md;

  &__head {
    display: flex;
    align-items: baseline;
    gap: 12rpx;
    margin-bottom: $fm-gap-md;
  }

  &__mark {
    font-family: $cy-mono;
    font-size: 18rpx;
    font-weight: 700;
    color: $cy-void;
    background: $cy-cyan;
    padding: 2rpx 10rpx;
    border-radius: $fm-radius-sm;
  }

  &__title {
    font-size: 28rpx;
    font-weight: 700;
  }

  &__copy {
    margin-left: auto;
    font-family: $cy-mono;
    font-size: 21rpx;
    color: $cy-cyan;
  }
}

.group {
  margin-top: $fm-gap-md;

  &__name {
    display: block;
    font-family: $cy-mono;
    font-size: 21rpx;
    letter-spacing: 2rpx;
    color: $cy-amber;
    margin-bottom: 8rpx;
  }

  &__items {
    display: flex;
    flex-direction: column;
  }
}

.item {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: $fm-gap-md;
  padding: 14rpx 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);

  &:last-child {
    border-bottom: none;
  }

  &__main {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }

  &__name {
    font-size: 27rpx;
    color: $cy-text;
  }

  &__for {
    margin-top: 2rpx;
    font-family: $cy-mono;
    font-size: 19rpx;
    color: $cy-text-faint;
  }

  &__amount {
    font-family: $cy-mono;
    font-size: 24rpx;
    color: $cy-cyan;
    flex-shrink: 0;
  }
}

.stub {
  margin-top: $fm-gap-md;
  padding-top: $fm-gap-md;
  border-top: 1px solid $cy-line;
  font-size: 21rpx;
  line-height: 1.7;
  color: $cy-text-faint;
}

.err {
  margin-top: $fm-gap-md;
  font-family: $cy-mono;
  font-size: 22rpx;
  color: $cy-red;
}

/* ── 搜索 ─────────────────────────────────────────────────── */
.search {
  display: flex;
  align-items: center;
  gap: 12rpx;
  margin-top: $fm-gap-lg;
  padding: 0 $fm-gap-md;
  height: 80rpx;
  background: $cy-surface-2;
  border: 1px solid rgba(0, 240, 255, 0.3);
  border-radius: $fm-radius-md;

  &__icon {
    font-size: 24rpx;
    opacity: 0.7;
  }

  &__input {
    flex: 1;
    height: 100%;
    font-size: 26rpx;
    color: $cy-text;
  }
}

.ph {
  color: $cy-text-faint;
}

.dim {
  padding: 40rpx 0;
  text-align: center;
  font-family: $cy-mono;
  font-size: 22rpx;
  color: $cy-text-faint;
}

/* ── 候选 ─────────────────────────────────────────────────── */
.cands {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-sm;
  margin-top: $fm-gap-md;
}

.cand {
  display: flex;
  align-items: center;
  gap: $fm-gap-md;
  padding: $fm-gap-md;
  background: $cy-surface;
  border: 1px solid $cy-line;
  border-radius: $fm-radius-md;
  transition: all 0.16s ease;

  &--on {
    border-color: $cy-cyan;
    background: rgba(0, 240, 255, 0.06);
    box-shadow: 0 0 14rpx rgba(0, 240, 255, 0.25);
  }

  &__body {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }

  &__name {
    font-size: 28rpx;
    font-weight: 700;
  }

  &__meta {
    margin-top: 4rpx;
    font-family: $cy-mono;
    font-size: 20rpx;
    color: $cy-text-faint;
  }

  &__check {
    font-family: $cy-mono;
    font-size: 32rpx;
    color: $cy-cyan;
    text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.7);
  }
}
</style>
