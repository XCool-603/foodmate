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

  /* 技术网格 + 暗角：营造「透过一块屏幕在看」的质感 */
  background-image:
    radial-gradient(ellipse at 50% 0%, rgba(0, 240, 255, 0.08) 0%, transparent 55%),
    radial-gradient(ellipse at 50% 100%, rgba(255, 46, 151, 0.07) 0%, transparent 55%),
    linear-gradient(rgba(0, 240, 255, 0.05) 1px, transparent 1px),
    linear-gradient(90deg, rgba(0, 240, 255, 0.05) 1px, transparent 1px);
  background-size:
    100% 100%,
    100% 100%,
    44rpx 44rpx,
    44rpx 44rpx;
}

/* ── 面板 ─────────────────────────────────────────────────── */
.fm-card {
  position: relative;
  padding: $fm-gap-lg;
  margin-bottom: $fm-gap-md;
  border-radius: $fm-radius-lg;
  background:
    linear-gradient(150deg, rgba(0, 240, 255, 0.08) 0%, transparent 45%),
    $cy-surface;

  /* 带电边框：不是中性灰，而是暗青 */
  border: 1px solid rgba(0, 240, 255, 0.3);
  box-shadow:
    0 0 16rpx rgba(0, 240, 255, 0.16),
    inset 0 0 48rpx rgba(0, 240, 255, 0.06);
  overflow: hidden;

  /* 四角 HUD 框：像瞄准镜一样把内容框住 */
  &::before,
  &::after {
    content: '';
    position: absolute;
    width: 22rpx;
    height: 22rpx;
    pointer-events: none;
  }

  &::before {
    top: 10rpx;
    left: 10rpx;
    border-top: 2px solid rgba(0, 240, 255, 0.85);
    border-left: 2px solid rgba(0, 240, 255, 0.85);
    border-top-left-radius: 8rpx;
  }

  &::after {
    bottom: 10rpx;
    right: 10rpx;
    border-bottom: 2px solid rgba(255, 46, 151, 0.75);
    border-right: 2px solid rgba(255, 46, 151, 0.75);
    border-bottom-right-radius: 8rpx;
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
  border-radius: $fm-radius-pill;
  background: $cy-surface-2;
  border: 1px solid rgba(0, 240, 255, 0.22);
  color: $cy-text-dim;
  font-size: 25rpx;
  font-family: $cy-mono;
  letter-spacing: 1rpx;
  transition: all 0.16s ease;

  &--active {
    background: rgba(0, 240, 255, 0.16);
    border-color: $cy-cyan;
    color: $cy-cyan;
    font-weight: 700;
    text-shadow: 0 0 10rpx rgba(0, 240, 255, 0.85);
    box-shadow:
      0 0 4rpx rgba(0, 240, 255, 0.9),
      0 0 18rpx rgba(0, 240, 255, 0.5),
      inset 0 0 20rpx rgba(0, 240, 255, 0.18);
  }
}

/* ── 主按钮 ───────────────────────────────────────────────── */
.fm-button {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  height: 96rpx;
  border-radius: $fm-radius-md;
  background: linear-gradient(100deg, rgba(0, 240, 255, 0.22), rgba(255, 46, 151, 0.18));
  border: 1px solid $cy-cyan;
  color: $cy-cyan;
  font-size: 30rpx;
  font-weight: 800;
  letter-spacing: 4rpx;
  text-shadow:
    0 0 6rpx rgba(0, 240, 255, 0.95),
    0 0 20rpx rgba(0, 240, 255, 0.6);
  box-shadow:
    0 0 4rpx rgba(0, 240, 255, 0.9),
    0 0 20rpx rgba(0, 240, 255, 0.5),
    0 0 46rpx rgba(0, 240, 255, 0.22),
    inset 0 0 30rpx rgba(0, 240, 255, 0.14);
  overflow: hidden;
  /* 霓虹管通电后的轻微呼吸 */
  animation: fm-neon-breathe 2.6s ease-in-out infinite;

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
      rgba(255, 255, 255, 0.3),
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
  text-shadow:
    0 0 6rpx rgba(0, 240, 255, 0.95),
    0 0 20rpx rgba(0, 240, 255, 0.55);
}

/* 信号失真文字：用于大标题 */
.cy-glitch {
  @include chromatic(2rpx);
  animation: fm-signal-loss 5.5s steps(1) infinite;
}

/* 霓虹分隔线 */
.cy-divider {
  height: 1px;
  background: linear-gradient(
    90deg,
    transparent,
    rgba(0, 240, 255, 0.8),
    transparent
  );
  box-shadow: 0 0 12rpx rgba(0, 240, 255, 0.6);
  margin: $fm-gap-md 0;
}

/* 危险态 */
.cy-danger {
  color: $cy-red;
  text-shadow: 0 0 12rpx rgba(255, 59, 92, 0.5);
}

/* ═══════════════════════════════════════════════════════════
 *  动画系统
 *
 *  原则：动效服务于「状态变化」，不是装饰。
 *    · 进场 → 让用户知道内容来了（fade-up / pop）
 *    · 加载 → 让等待有反馈（shimmer 骨架屏）
 *    · 强调 → 让重要数字被看见（count-up / glow）
 *    · 反馈 → 让点击有回应（press 缩放）
 *  所有时长控制在 0.2–0.6s，超过 0.6s 就会显得拖沓。
 * ═══════════════════════════════════════════════════════════ */

/* ── 关键帧 ───────────────────────────────────────────────── */

/* 淡入上移：最常用的进场 */
@keyframes fm-fade-up {
  from {
    opacity: 0;
    transform: translateY(26rpx);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* 淡入（无位移）：用于不适合移动的元素 */
@keyframes fm-fade {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

/* 弹入：带轻微过冲，用于卡片、徽标 */
@keyframes fm-pop {
  0% {
    opacity: 0;
    transform: scale(0.8);
  }
  62% {
    opacity: 1;
    transform: scale(1.05);
  }
  100% {
    opacity: 1;
    transform: scale(1);
  }
}

/* 从右侧滑入 */
@keyframes fm-slide-right {
  from {
    opacity: 0;
    transform: translateX(40rpx);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
}

/* 骨架屏微光扫过 */
@keyframes fm-shimmer {
  0% {
    background-position: -180% 0;
  }
  100% {
    background-position: 180% 0;
  }
}

/* 霓虹呼吸 */
@keyframes fm-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.5;
  }
}

/* 霓虹管通电呼吸：亮度轻微起伏，模拟镇流器的抖动 */
@keyframes fm-neon-breathe {
  0%,
  100% {
    filter: brightness(1);
  }
  48% {
    filter: brightness(1.12);
  }
  52% {
    filter: brightness(0.94);
  }
}

/* 信号失真：偶发的 RGB 错位，赛博朋克标志性故障 */
@keyframes fm-signal-loss {
  0%,
  88%,
  100% {
    text-shadow:
      -2rpx 0 0 rgba(255, 46, 151, 0.75),
      2rpx 0 0 rgba(0, 240, 255, 0.75);
    transform: translate(0);
  }
  90% {
    text-shadow:
      -6rpx 0 0 rgba(255, 46, 151, 0.95),
      6rpx 0 0 rgba(0, 240, 255, 0.95);
    transform: translate(-2rpx, 1rpx);
  }
  92% {
    text-shadow:
      4rpx 0 0 rgba(255, 46, 151, 0.9),
      -4rpx 0 0 rgba(0, 240, 255, 0.9);
    transform: translate(2rpx, -1rpx);
  }
  94% {
    text-shadow:
      -2rpx 0 0 rgba(255, 46, 151, 0.75),
      2rpx 0 0 rgba(0, 240, 255, 0.75);
    transform: translate(0);
  }
}

/* 缓慢浮动 */
@keyframes fm-float {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-10rpx);
  }
}

/* 成功庆祝：一次性的放大回弹 */
@keyframes fm-celebrate {
  0% {
    transform: scale(0.6);
    opacity: 0;
  }
  45% {
    transform: scale(1.12);
    opacity: 1;
  }
  70% {
    transform: scale(0.97);
  }
  100% {
    transform: scale(1);
    opacity: 1;
  }
}

/* 按钮扫光 */
@keyframes fm-sweep {
  0%,
  60% {
    left: -60%;
  }
  100% {
    left: 130%;
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

/* ── 工具类 ───────────────────────────────────────────────── */

/* 统一缓动：先快后慢，有"落位"感 */
$fm-ease: cubic-bezier(0.22, 1, 0.36, 1);

.anim-in {
  animation: fm-fade-up 0.44s $fm-ease both;
}

.anim-fade {
  animation: fm-fade 0.4s ease both;
}

.anim-pop {
  animation: fm-pop 0.46s $fm-ease both;
}

.anim-slide {
  animation: fm-slide-right 0.42s $fm-ease both;
}

.anim-float {
  animation: fm-float 3s ease-in-out infinite;
}

.anim-glow {
  animation: fm-pulse 1.8s ease-in-out infinite;
}

.anim-celebrate {
  animation: fm-celebrate 0.62s $fm-ease both;
}

/**
 * 交错入场：给容器加这个类，子元素会依次进场。
 * 只定义到第 12 个 —— 再多用户也感知不到差别，反而显得慢。
 */
.anim-stagger > * {
  animation: fm-fade-up 0.42s $fm-ease both;
}

.anim-stagger > *:nth-child(1) { animation-delay: 0.02s; }
.anim-stagger > *:nth-child(2) { animation-delay: 0.06s; }
.anim-stagger > *:nth-child(3) { animation-delay: 0.1s; }
.anim-stagger > *:nth-child(4) { animation-delay: 0.14s; }
.anim-stagger > *:nth-child(5) { animation-delay: 0.18s; }
.anim-stagger > *:nth-child(6) { animation-delay: 0.22s; }
.anim-stagger > *:nth-child(7) { animation-delay: 0.26s; }
.anim-stagger > *:nth-child(8) { animation-delay: 0.3s; }
.anim-stagger > *:nth-child(9) { animation-delay: 0.34s; }
.anim-stagger > *:nth-child(10) { animation-delay: 0.38s; }
.anim-stagger > *:nth-child(11) { animation-delay: 0.42s; }
.anim-stagger > *:nth-child(12) { animation-delay: 0.46s; }

/**
 * 按压反馈。
 * 用 uni-app 的 hover-class 而不是 CSS :active ——
 * 后者在小程序端不可靠，hover-class 是三端都支持的机制。
 */
.hover-dim {
  opacity: 0.78;
  transform: scale(0.982);
}

.hover-lift {
  transform: translateY(-3rpx);
  box-shadow: 0 6rpx 22rpx rgba(0, 240, 255, 0.22);
}

/* 骨架屏基底 */
.skeleton {
  background: linear-gradient(
    100deg,
    $cy-surface-2 30%,
    rgba(0, 240, 255, 0.09) 50%,
    $cy-surface-2 70%
  );
  background-size: 220% 100%;
  animation: fm-shimmer 1.5s linear infinite;
  border-radius: $fm-radius-sm;
}
</style>
