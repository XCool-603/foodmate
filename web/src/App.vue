<script setup lang="ts">
import { onLaunch, onShow } from '@dcloudio/uni-app'
import { useAuthStore, useMetaStore } from '@/stores'

onLaunch(async () => {
  const meta = useMetaStore()
  const auth = useAuthStore()

  // 启动即探测后端连通性并预取枚举字典，避免用户点进页面才等待
  void meta.checkConnection()
  void meta.loadEnums()

  // 静默登录：有令牌先验一次，失效或没有则走各端授权
  await auth.ensureLoggedIn()
})

onShow(() => {
  // 预留：从后台切回时刷新连接状态
})
</script>

<style lang="scss">
page {
  background-color: $fm-bg-page;
  color: $fm-text;
  font-size: 28rpx;
  line-height: 1.6;
  font-family: -apple-system, BlinkMacSystemFont, 'Helvetica Neue', 'PingFang SC',
    'Hiragino Sans GB', 'Microsoft YaHei', sans-serif;
}

/* ── 通用卡片 ── */
.fm-card {
  background: $fm-bg-card;
  border-radius: $fm-radius-lg;
  padding: $fm-gap-lg;
  margin-bottom: $fm-gap-md;
}

/* ── 通用标签块 ── */
.fm-chips {
  display: flex;
  flex-wrap: wrap;
  gap: $fm-gap-sm;
}

.fm-chip {
  padding: 14rpx 28rpx;
  border-radius: 999rpx;
  background: $fm-bg-muted;
  color: $fm-text-secondary;
  font-size: 26rpx;
  transition: all 0.15s ease;

  &--active {
    background: $fm-primary-soft;
    color: $fm-primary;
    font-weight: 600;
  }
}

/* ── 主按钮 ── */
.fm-button {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 96rpx;
  border-radius: 999rpx;
  background: $fm-primary;
  color: $fm-text-inverse;
  font-size: 32rpx;
  font-weight: 600;

  &--ghost {
    background: transparent;
    color: $fm-primary;
    border: 2rpx solid $fm-primary;
  }

  &--disabled {
    background: #d9dbe0;
    color: #ffffff;
  }
}
</style>
