<script setup lang="ts">
/**
 * 骨架屏。
 *
 * 比「加载中…」三个字强得多：它提前把内容的形状画出来，
 * 用户能预判即将出现什么，等待感明显降低。
 */
withDefaults(
  defineProps<{
    /** 行数 */
    rows?: number
    /** 是否显示顶部的大块（模拟图片/头图） */
    hero?: boolean
    /** 头像圆点 */
    avatar?: boolean
  }>(),
  { rows: 3, hero: false, avatar: false },
)
</script>

<template>
  <view class="sk">
    <!-- 头图 -->
    <view v-if="hero" class="sk__hero skeleton" />

    <!-- 头像 + 两行 -->
    <view v-if="avatar" class="sk__row">
      <view class="sk__avatar skeleton" />
      <view class="sk__lines">
        <view class="sk__line skeleton" style="width: 46%" />
        <view class="sk__line sk__line--sm skeleton" style="width: 28%" />
      </view>
    </view>

    <!-- 普通行 -->
    <view v-for="i in rows" :key="i" class="sk__block">
      <view class="sk__line skeleton" :style="{ width: `${88 - (i % 3) * 14}%` }" />
      <view class="sk__line sk__line--sm skeleton" :style="{ width: `${54 + (i % 2) * 12}%` }" />
    </view>
  </view>
</template>

<style lang="scss" scoped>
.sk {
  display: flex;
  flex-direction: column;
  gap: $fm-gap-md;

  &__hero {
    width: 100%;
    height: 300rpx;
    border-radius: $fm-radius-lg;
  }

  &__row {
    display: flex;
    align-items: center;
    gap: $fm-gap-md;
  }

  &__avatar {
    width: 128rpx;
    height: 128rpx;
    border-radius: $fm-radius-md;
    flex-shrink: 0;
  }

  &__lines {
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 12rpx;
  }

  &__block {
    display: flex;
    flex-direction: column;
    gap: 12rpx;
    padding: $fm-gap-md;
    border: 1px solid $cy-line;
    border-radius: $fm-radius-md;
  }

  &__line {
    height: 26rpx;
    border-radius: $fm-radius-sm;

    &--sm {
      height: 20rpx;
      opacity: 0.6;
    }
  }
}
</style>
