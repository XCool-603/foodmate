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
/* ═══════════════════════════════════════════════════════════
 *  全局样式 · 赛博朋克
 *
 *  页面只依赖下面这几个通用类（.fm-card / .fm-chip / .fm-button），
 *  因此换肤的主要成本集中在这里。
 * ═══════════════════════════════════════════════════════════ */

page {
  background-color: $cy-void;
  color: $cy-text;
  font-size: 28rpx;
  line-height: 1.6;
  font-family: -apple-system, BlinkMacSystemFont, 'Helvetica Neue', 'PingFang SC',
    'Hiragino Sans GB', 'Microsoft YaHei', sans-serif;

  /* 技术网格底纹：极淡，只提供"这是块屏幕"的暗示 */
  background-image:
    linear-gradient(rgba(0, 240, 255, 0.028) 1px, transparent 1px),
    linear-gradient(90deg, rgba(0, 240, 255, 0.028) 1px, transparent 1px);
  background-size: 44rpx 44rpx;
}

/* ── 面板 ─────────────────────────────────────────────────── */
.fm-card {
  position: relative;
  padding: $fm-gap-lg;
  margin-bottom: $fm-gap-md;
  background:
    linear-gradient(150deg, rgba(0, 240, 255, 0.055) 0%, transparent 45%),
    $cy-surface;
  border: 1px solid $cy-line;

  /* 右上角 L 形角标 —— 比圆角更"仪器" */
  &::after {
    content: '';
    position: absolute;
    top: -1px;
    right: -1px;
    width: 20rpx;
    height: 20rpx;
    border-top: 2px solid rgba(0, 240, 255, 0.6);
    border-right: 2px solid rgba(0, 240, 255, 0.6);
    pointer-events: none;
  }
}

/* ── 标签块 ───────────────────────────────────────────────── */
.fm-chips {
  display: flex;
  flex-wrap: wrap;
  gap: $fm-gap-sm;
}

.fm-chip {
  position: relative;
  padding: 12rpx 26rpx;
  background: $cy-surface-2;
  border: 1px solid $cy-line;
  color: $cy-text-dim;
  font-size: 25rpx;
  font-family: $cy-mono;
  letter-spacing: 1rpx;
  transition: all 0.16s ease;

  &--active {
    background: rgba(0, 240, 255, 0.1);
    border-color: $cy-cyan;
    color: $cy-cyan;
    font-weight: 600;
    box-shadow:
      0 0 10rpx rgba(0, 240, 255, 0.45),
      inset 0 0 14rpx rgba(0, 240, 255, 0.12);
  }
}

/* ── 主按钮 ───────────────────────────────────────────────── */
.fm-button {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  height: 96rpx;
  background: linear-gradient(100deg, rgba(0, 240, 255, 0.16), rgba(255, 46, 151, 0.12));
  border: 1px solid $cy-cyan;
  color: $cy-cyan;
  font-size: 30rpx;
  font-weight: 700;
  letter-spacing: 3rpx;
  text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.6);
  box-shadow:
    0 0 14rpx rgba(0, 240, 255, 0.35),
    inset 0 0 22rpx rgba(0, 240, 255, 0.1);
  overflow: hidden;

  /* 按钮内的斜向扫光 */
  &::before {
    content: '';
    position: absolute;
    top: 0;
    left: -60%;
    width: 40%;
    height: 100%;
    background: linear-gradient(
      100deg,
      transparent,
      rgba(255, 255, 255, 0.22),
      transparent
    );
    animation: fm-sweep 3.4s ease-in-out infinite;
    pointer-events: none;
  }

  &--ghost {
    background: transparent;
    border-color: rgba(0, 240, 255, 0.4);
    color: $cy-cyan;
    box-shadow: none;
    text-shadow: none;
    font-weight: 600;
    letter-spacing: 1rpx;
  }

  &--disabled {
    background: $cy-surface-2;
    border-color: $cy-line;
    color: $cy-text-faint;
    text-shadow: none;
    box-shadow: none;

    &::before {
      animation: none;
    }
  }
}

/* ── HUD 工具类 ───────────────────────────────────────────── */

/* 等宽小标签：`> 决策条件` */
.cy-label {
  display: inline-block;
  font-family: $cy-mono;
  font-size: 20rpx;
  letter-spacing: 2rpx;
  color: $cy-text-faint;
  text-transform: uppercase;
}

/* 数值：等宽 + 霓虹 */
.cy-value {
  font-family: $cy-mono;
  font-weight: 700;
  color: $cy-cyan;
  text-shadow: 0 0 12rpx rgba(0, 240, 255, 0.5);
}

/* 细霓虹分隔线 */
.cy-divider {
  height: 1px;
  background: linear-gradient(
    90deg,
    transparent,
    rgba(0, 240, 255, 0.4),
    transparent
  );
  margin: $fm-gap-md 0;
}

/* 危险态 */
.cy-danger {
  color: $cy-red;
  text-shadow: 0 0 12rpx rgba(255, 59, 92, 0.5);
}

/* ── 动画 ─────────────────────────────────────────────────── */
@keyframes fm-sweep {
  0%,
  60% {
    left: -60%;
  }
  100% {
    left: 130%;
  }
}

/* 霓虹呼吸：用于强调"进行中"的元素 */
@keyframes fm-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.55;
  }
}

/* 故障闪烁：标题用 */
@keyframes fm-glitch {
  0%,
  92%,
  100% {
    transform: translate(0);
    opacity: 1;
  }
  93% {
    transform: translate(-2rpx, 1rpx);
    opacity: 0.85;
  }
  95% {
    transform: translate(2rpx, -1rpx);
    opacity: 0.95;
  }
  97% {
    transform: translate(-1rpx, -1rpx);
    opacity: 0.9;
  }
}
</style>
