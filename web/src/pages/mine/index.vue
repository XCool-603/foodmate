<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { profileApi } from '@/api'
import type { ProfileSummary } from '@/api'
import { useAuthStore, useMetaStore } from '@/stores'

const meta = useMetaStore()
const auth = useAuthStore()

const summary = ref<ProfileSummary | null>(null)
const checking = ref(false)

const authText = computed(() => {
  switch (auth.status) {
    case 'authenticated':
      return auth.user?.platformLabel ? `${auth.user.platformLabel}登录` : '已登录'
    case 'logging-in':
      return '登录中…'
    case 'failed':
      return '登录失败'
    default:
      return '未登录'
  }
})

async function onRetryLogin() {
  const ok = await auth.ensureLoggedIn()
  uni.showToast({ title: ok ? '已登录' : '登录失败', icon: 'none' })

  if (ok) void load()
}

async function onLogout() {
  const confirmed = await new Promise<boolean>((resolve) => {
    uni.showModal({
      title: '退出登录',
      content: '退出后需要重新授权才能同步记录。确定退出吗？',
      success: (res) => resolve(Boolean(res.confirm)),
      fail: () => resolve(false),
    })
  })

  if (!confirmed) return

  await auth.logout()
  summary.value = null

  uni.showToast({ title: '已退出', icon: 'none' })
}

const stateText = computed(() => {
  switch (meta.state) {
    case 'online':
      return '已连接'
    case 'offline':
      return '未连接'
    case 'checking':
      return '检查中…'
    default:
      return '未知'
  }
})

const lastCheckedText = computed(() => {
  if (!meta.lastCheckedAt) return '尚未检查'
  const d = new Date(meta.lastCheckedAt)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
})

const enumSummary = computed(() => {
  const e = meta.enums
  if (!e) return []
  return [
    { label: '就餐方式', count: e.diningMode.length },
    { label: '菜系', count: e.cuisine.length },
    { label: '菜品分类', count: e.dishCategory.length },
    { label: '辣度档位', count: e.spicyLevel.length },
    { label: '快捷标签', count: e.moodTag.length },
    { label: '常见过敏原', count: e.avoidIngredient.length },
  ]
})

async function load() {
  void meta.checkConnection()
  void meta.loadEnums()

  // 未登录时先补登录，否则下面的请求必然 401
  if (!auth.isAuthenticated) {
    const ok = await auth.ensureLoggedIn()
    if (!ok) {
      summary.value = null
      return
    }
  }

  try {
    summary.value = await profileApi.summary()
  } catch {
    summary.value = null
  }
}

function onOpenPreference() {
  uni.navigateTo({ url: '/pages/mine/preference' })
}

function onOpenRecords() {
  uni.switchTab({ url: '/pages/records/index' })
}

async function onRecheck() {
  checking.value = true
  await meta.checkConnection()
  await meta.loadEnums(true)
  checking.value = false
  uni.showToast({ title: meta.isOnline ? '连接正常' : '仍然连不上', icon: 'none' })
}

function onCopyUrl() {
  uni.setClipboardData({
    data: meta.apiBaseUrl,
    success: () => uni.showToast({ title: '已复制 API 地址', icon: 'none' }),
  })
}

onShow(() => {
  void load()
})
</script>

<template>
  <view class="page">
    <!-- 账号 -->
    <view class="fm-card account">
      <view class="account__avatar">
        <image
          v-if="auth.user?.avatarUrl"
          class="account__image"
          :src="auth.user.avatarUrl"
          mode="aspectFill"
        />
        <text v-else class="account__placeholder">🍜</text>
      </view>
      <view class="account__info">
        <text class="account__name">{{ auth.user?.nickname ?? '未登录' }}</text>
        <text class="account__meta">{{ authText }}</text>
      </view>
      <view v-if="auth.isAuthenticated" class="account__action" @tap="onLogout">退出</view>
      <view v-else class="account__action account__action--primary" @tap="onRetryLogin">
        {{ auth.isLoggingIn ? '登录中…' : '登录' }}
      </view>
    </view>

    <!-- 数据卡片 -->
    <view class="fm-card hero">
      <text class="hero__greeting">你的美食足迹</text>
      <view class="hero__row">
        <view class="hero__item">
          <text class="hero__value">{{ summary?.totalRecords ?? 0 }}</text>
          <text class="hero__label">记录餐数</text>
        </view>
        <view class="hero__divider" />
        <view class="hero__item">
          <text class="hero__value">{{ summary?.totalDishesTried ?? 0 }}</text>
          <text class="hero__label">尝过菜品</text>
        </view>
        <view class="hero__divider" />
        <view class="hero__item">
          <text class="hero__value">{{ summary?.currentStreakDays ?? 0 }}</text>
          <text class="hero__label">连续打卡</text>
        </view>
      </view>
      <view v-if="summary?.favoriteCuisine || summary?.averageRating" class="hero__extra">
        <text v-if="summary?.favoriteCuisine">
          最常吃 {{ summary.favoriteCuisine.label }}（{{ summary.favoriteCuisine.count }} 次）
        </text>
        <text v-if="summary?.averageRating"> · 平均 {{ summary.averageRating }} 分</text>
      </view>
    </view>

    <!-- 入口 -->
    <view class="fm-card menu">
      <view class="menu__item" @tap="onOpenPreference">
        <text class="menu__label">口味画像</text>
        <text class="menu__hint">辣度 · 预算 · 忌口 · 偏好菜系</text>
        <text class="menu__arrow">›</text>
      </view>
      <view class="menu__item" @tap="onOpenRecords">
        <text class="menu__label">饮食记录</text>
        <text class="menu__hint">共 {{ summary?.totalRecords ?? 0 }} 条</text>
        <text class="menu__arrow">›</text>
      </view>
    </view>

    <!-- 连接诊断 -->
    <view class="fm-card">
      <view class="card-head">
        <text class="card-title">后端连接</text>
        <text class="badge" :class="`badge--${meta.state}`">{{ stateText }}</text>
      </view>

      <view class="kv" @tap="onCopyUrl">
        <text class="kv__key">API 地址</text>
        <text class="kv__value kv__value--link">{{ meta.apiBaseUrl }}</text>
      </view>
      <view class="kv">
        <text class="kv__key">运行平台</text>
        <text class="kv__value">{{ meta.platformLabel }}端</text>
      </view>
      <view class="kv">
        <text class="kv__key">服务版本</text>
        <text class="kv__value">{{ meta.health ? `v${meta.health.version}` : '—' }}</text>
      </view>
      <view class="kv">
        <text class="kv__key">菜品库</text>
        <text class="kv__value">{{ meta.dishCount }} 道</text>
      </view>
      <view class="kv">
        <text class="kv__key">上次检查</text>
        <text class="kv__value">{{ lastCheckedText }}</text>
      </view>

      <view v-if="meta.errorMessage" class="error-box">
        <text class="error-box__text">{{ meta.errorMessage }}</text>
        <text class="error-box__hint">
          请确认已运行：dotnet run --project src/FoodMate.Api
        </text>
      </view>

      <view
        class="fm-button fm-button--ghost recheck"
        :class="{ 'fm-button--disabled': checking }"
        @tap="onRecheck"
      >
        {{ checking ? '检查中…' : '重新检查' }}
      </view>
    </view>

    <!-- 枚举字典 -->
    <view class="fm-card">
      <view class="card-head">
        <text class="card-title">枚举字典</text>
        <text class="card-hint">{{ meta.enums ? '已加载' : '未加载' }}</text>
      </view>
      <view v-if="enumSummary.length" class="enum-grid">
        <view v-for="item in enumSummary" :key="item.label" class="enum-grid__cell">
          <text class="enum-grid__count">{{ item.count }}</text>
          <text class="enum-grid__label">{{ item.label }}</text>
        </view>
      </view>
      <view v-else class="card-desc">尚未加载枚举字典。</view>
    </view>

    <view class="footer">美食伴侣 · M2 · 0.2.0</view>
  </view>
</template>

<style lang="scss" scoped>
.page {
  padding: $fm-gap-md;
}

/* ── 账号 ── */
.account {
  display: flex;
  align-items: center;
  gap: $fm-gap-md;

  &__avatar {
    width: 96rpx;
    height: 96rpx;
    border-radius: 50%;
    background: $fm-primary-soft;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    flex-shrink: 0;
  }

  &__image {
    width: 100%;
    height: 100%;
  }

  &__placeholder {
    font-size: 44rpx;
    line-height: 1;
  }

  &__info {
    flex: 1;
    display: flex;
    flex-direction: column;
  }

  &__name {
    font-size: 32rpx;
    font-weight: 600;
  }

  &__meta {
    margin-top: 4rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }

  &__action {
    font-size: 25rpx;
    color: $fm-text-tertiary;
    padding: 12rpx 28rpx;
    border-radius: 999rpx;
    background: $fm-bg-muted;

    &--primary {
      background: $fm-primary;
      color: #fff;
      font-weight: 600;
    }
  }
}

/* ── 数据卡片 ── */
.hero {
  &__greeting {
    display: block;
    font-size: 26rpx;
    color: $fm-text-secondary;
    margin-bottom: $fm-gap-md;
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
    font-size: 48rpx;
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
    height: 60rpx;
    background: $fm-border;
  }

  &__extra {
    margin-top: $fm-gap-md;
    padding-top: $fm-gap-md;
    border-top: 2rpx solid $fm-border;
    font-size: 23rpx;
    color: $fm-text-tertiary;
    text-align: center;
  }
}

/* ── 菜单 ── */
.menu {
  padding: 0 $fm-gap-lg;

  &__item {
    display: flex;
    align-items: center;
    padding: $fm-gap-lg 0;
    border-bottom: 2rpx solid $fm-border;

    &:last-child {
      border-bottom: none;
    }
  }

  &__label {
    font-size: 29rpx;
    font-weight: 600;
  }

  &__hint {
    flex: 1;
    margin-left: $fm-gap-md;
    font-size: 22rpx;
    color: $fm-text-tertiary;
    text-align: right;
  }

  &__arrow {
    margin-left: $fm-gap-sm;
    font-size: 36rpx;
    color: $fm-text-tertiary;
  }
}

/* ── 诊断 ── */
.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: $fm-gap-md;
}

.card-title {
  font-size: 30rpx;
  font-weight: 600;
}

.card-hint {
  font-size: 24rpx;
  color: $fm-text-tertiary;
}

.card-desc {
  display: block;
  font-size: 24rpx;
  color: $fm-text-tertiary;
  line-height: 1.7;
}

.badge {
  font-size: 22rpx;
  padding: 6rpx 18rpx;
  border-radius: 999rpx;

  &--online {
    background: rgba(48, 163, 108, 0.12);
    color: $fm-success;
  }

  &--offline {
    background: rgba(229, 72, 77, 0.12);
    color: $fm-danger;
  }

  &--checking,
  &--unknown {
    background: rgba(245, 166, 35, 0.14);
    color: $fm-warning;
  }
}

.kv {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: $fm-gap-md;
  padding: 14rpx 0;
  border-bottom: 2rpx solid $fm-border;

  &:last-of-type {
    border-bottom: none;
  }

  &__key {
    font-size: 26rpx;
    color: $fm-text-secondary;
    flex-shrink: 0;
  }

  &__value {
    font-size: 26rpx;
    text-align: right;
    word-break: break-all;

    &--link {
      color: $fm-primary;
      text-decoration: underline;
    }
  }
}

.error-box {
  margin-top: $fm-gap-md;
  padding: $fm-gap-md;
  border-radius: $fm-radius-md;
  background: rgba(229, 72, 77, 0.08);

  &__text {
    display: block;
    font-size: 24rpx;
    color: $fm-danger;
  }

  &__hint {
    display: block;
    margin-top: 8rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
    font-family: 'SF Mono', Consolas, Monaco, monospace;
  }
}

.recheck {
  margin-top: $fm-gap-lg;
}

.enum-grid {
  display: flex;
  flex-wrap: wrap;

  &__cell {
    width: 33.33%;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 16rpx 0;
  }

  &__count {
    font-size: 36rpx;
    font-weight: 700;
    color: $fm-primary;
  }

  &__label {
    margin-top: 4rpx;
    font-size: 22rpx;
    color: $fm-text-tertiary;
  }
}

.footer {
  margin-top: $fm-gap-lg;
  text-align: center;
  font-size: 22rpx;
  color: $fm-text-tertiary;
}
</style>
