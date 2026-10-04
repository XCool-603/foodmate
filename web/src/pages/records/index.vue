<script setup lang="ts">
import { computed } from 'vue'
import { onPullDownRefresh, onReachBottom, onShow } from '@dcloudio/uni-app'
import { useMetaStore, useRecordStore } from '@/stores'
import { formatPriceRange } from '@/utils/format'

const meta = useMetaStore()
const records = useRecordStore()

const weekLabel = computed(() => {
  const stats = records.stats
  if (!stats) return '本周'
  return `${stats.from.slice(5).replace('-', '/')} – ${stats.to.slice(5).replace('-', '/')}`
})

async function refreshAll() {
  if (!meta.isOnline) {
    await meta.checkConnection()
  }

  await Promise.all([
    records.load(),
    records.loadStats(),
    records.loadPending(),
  ])
}

function onAdd() {
  uni.showActionSheet({
    itemList: ['拍照识别', '手动记录'],
    success: ({ tapIndex }) => {
      uni.navigateTo({
        url: tapIndex === 0 ? '/pages/ai/recognize' : '/pages/records/edit',
      })
    },
  })
}

function onEdit(id: string) {
  uni.navigateTo({ url: `/pages/records/edit?id=${id}` })
}

/** 一键评分。 */
async function onQuickRate(id: string, rating: number) {
  try {
    await records.rate(id, rating, rating >= 4)
    uni.showToast({ title: '已记录评分', icon: 'none' })
  } catch (err) {
    uni.showToast({ title: (err as Error).message, icon: 'none' })
  }
}

/** 长按删除。 */
function onLongPress(item: { id: string; dishName: string }) {
  uni.showActionSheet({
    itemList: ['编辑', '删除'],
    success: async ({ tapIndex }) => {
      if (tapIndex === 0) {
        onEdit(item.id)
        return
      }

      const confirmed = await new Promise<boolean>((resolve) => {
        uni.showModal({
          title: '删除记录',
          content: `确定删除「${item.dishName}」这条记录吗？`,
          success: (res) => resolve(Boolean(res.confirm)),
          fail: () => resolve(false),
        })
      })

      if (!confirmed) return

      try {
        await records.remove(item.id)
        await records.loadStats()
        uni.showToast({ title: '已删除', icon: 'none' })
      } catch (err) {
        uni.showToast({ title: (err as Error).message, icon: 'none' })
      }
    },
  })
}

function stars(rating: number | null): string {
  if (!rating) return ''
  return '★'.repeat(rating) + '☆'.repeat(5 - rating)
}

onShow(() => {
  void refreshAll()
})

onPullDownRefresh(async () => {
  await refreshAll()
  uni.stopPullDownRefresh()
})

onReachBottom(() => {
  void records.loadMore()
})
</script>

<template>
  <view class="page">
    <!-- 本周统计 -->
    <view class="fm-card stats">
      <view class="stats__head">
        <text class="stats__title">{{ weekLabel }}</text>
        <text v-if="records.stats?.averageRating" class="stats__rating">
          平均 {{ records.stats.averageRating }} 分
        </text>
      </view>
      <view class="stats__row">
        <view class="stats__item">
          <text class="stats__value">{{ records.stats?.recordedDays ?? '—' }}</text>
          <text class="stats__label">打卡天数</text>
        </view>
        <view class="stats__divider" />
        <view class="stats__item">
          <text class="stats__value">{{ records.stats?.totalRecords ?? '—' }}</text>
          <text class="stats__label">记录餐数</text>
        </view>
        <view class="stats__divider" />
        <view class="stats__item">
          <text class="stats__value">{{ records.stats?.newDishesTried ?? '—' }}</text>
          <text class="stats__label">尝新菜品</text>
        </view>
      </view>
    </view>

    <!-- 待评分提醒 -->
    <view v-if="records.pending.length" class="pending">
      <text class="pending__title">给这几顿打个分？</text>
      <text class="pending__hint">你的评分会让下次推荐更准</text>
      <view v-for="item in records.pending" :key="item.id" class="pending__item">
        <view class="pending__info">
          <text class="pending__name">{{ item.dishName }}</text>
          <text class="pending__when">{{ item.daysAgo === 0 ? '今天' : `${item.daysAgo} 天前` }}</text>
        </view>
        <view class="pending__stars">
          <text
            v-for="n in 5"
            :key="n"
            class="pending__star"
            @tap="onQuickRate(item.id, n)"
          >★</text>
        </view>
      </view>
    </view>

    <!-- 记录列表 -->
    <view v-if="records.loading" class="empty">加载中…</view>

    <view v-else-if="!records.grouped.length" class="empty">
      <text class="empty__emoji">🍽️</text>
      <text class="empty__title">还没有任何记录</text>
      <text class="empty__desc">
        从「吃什么」页决定后一键记录，或点右下角手动添加。
      </text>
    </view>

    <view v-for="group in records.grouped" :key="group.date" class="group">
      <text class="group__label">{{ group.label }}</text>
      <view
        v-for="item in group.items"
        :key="item.id"
        class="record"
        @tap="onEdit(item.id)"
        @longpress="onLongPress(item)"
      >
        <view class="record__main">
          <text class="record__name">{{ item.dishName }}</text>
          <text class="record__meta">
            {{ item.mealTypeLabel }} · {{ item.diningModeLabel }}
            <template v-if="item.servings !== 1"> · {{ item.servings }} 份</template>
            <template v-if="item.calories"> · {{ item.calories }} kcal</template>
          </text>
          <text v-if="item.note" class="record__note">{{ item.note }}</text>
        </view>
        <view class="record__side">
          <text v-if="item.rating" class="record__stars">{{ stars(item.rating) }}</text>
          <text v-else class="record__unrated">未评分</text>
          <text v-if="item.dishSnapshot.priceCents" class="record__price">
            {{ formatPriceRange(item.dishSnapshot.priceCents, item.dishSnapshot.priceCents) }}
          </text>
        </view>
      </view>
    </view>

    <view v-if="records.loadingMore" class="empty">加载更多…</view>
    <view v-else-if="records.items.length && !records.hasMore" class="empty">没有更多了</view>

    <view class="fab" @tap="onAdd">＋</view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
  padding-bottom: 180rpx;
}

/* ── 统计 ── */
.stats {
  &__head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    margin-bottom: $fm-gap-md;
  }

  &__title {
    font-size: 28rpx;
    font-weight: 600;
  }

  &__rating {
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }

  &__row {
    display: flex;
    align-items: center;
  }

  &__item {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
  }

  &__value {
    font-size: 44rpx;
    font-weight: 700;
    color: $fm-primary;
  }

  &__label {
    margin-top: 4rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }

  &__divider {
    width: 2rpx;
    height: 56rpx;
    background: $fm-border;
  }
}

/* ── 待评分 ── */
.pending {
  background: $fm-primary-soft;
  border-radius: $fm-radius-lg;
  padding: $fm-gap-lg;
  margin-bottom: $fm-gap-md;

  &__title {
    font-size: 28rpx;
    font-weight: 600;
    color: $fm-primary;
  }

  &__hint {
    display: block;
    margin-top: 4rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }

  &__item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-top: $fm-gap-md;
  }

  &__info {
    display: flex;
    flex-direction: column;
  }

  &__name {
    font-size: 27rpx;
    font-weight: 600;
  }

  &__when {
    font-size: 21rpx;
    color: $fm-text-tertiary;
  }

  &__stars {
    display: flex;
    gap: 8rpx;
  }

  &__star {
    font-size: 40rpx;
    color: $fm-primary;
    padding: 0 4rpx;
  }
}

/* ── 分组 ── */
.group {
  margin-bottom: $fm-gap-md;

  &__label {
    display: block;
    margin: $fm-gap-md 8rpx $fm-gap-sm;
    font-size: 24rpx;
    font-weight: 600;
    color: $fm-text-secondary;
  }
}

.record {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: $fm-gap-md;
  background: $fm-bg-card;
  border-radius: $fm-radius-md;
  padding: $fm-gap-md $fm-gap-lg;
  margin-bottom: $fm-gap-sm;

  &__main {
    flex: 1;
    display: flex;
    flex-direction: column;
  }

  &__name {
    font-size: 29rpx;
    font-weight: 600;
  }

  &__meta {
    margin-top: 4rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }

  &__note {
    margin-top: 6rpx;
    font-size: 23rpx;
    color: $fm-text-secondary;
  }

  &__side {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    flex-shrink: 0;
  }

  &__stars {
    font-size: 24rpx;
    color: $fm-primary;
  }

  &__unrated {
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }

  &__price {
    margin-top: 6rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }
}

/* ── 空态与 FAB ── */
.empty {
  padding: 60rpx $fm-gap-lg;
  text-align: center;
  font-size: 25rpx;
  color: $fm-text-tertiary;

  &__emoji {
    display: block;
    font-size: 88rpx;
    line-height: 1;
    margin-bottom: $fm-gap-md;
  }

  &__title {
    display: block;
    font-size: 30rpx;
    font-weight: 600;
    color: $fm-text;
  }

  &__desc {
    display: block;
    margin-top: 8rpx;
    line-height: 1.7;
  }
}

.fab {
  position: fixed;
  right: 40rpx;
  bottom: 60rpx;
  width: 108rpx;
  height: 108rpx;
  background: rgba(0, 240, 255, 0.12);
  border: 1px solid $cy-cyan;
  color: $cy-cyan;
  font-size: 52rpx;
  font-weight: 300;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow:
    0 0 18rpx rgba(0, 240, 255, 0.45),
    inset 0 0 22rpx rgba(0, 240, 255, 0.12);
  text-shadow: 0 0 14rpx rgba(0, 240, 255, 0.8);
  box-shadow: 0 8rpx 24rpx rgba(255, 107, 53, 0.4);
}
</style>
