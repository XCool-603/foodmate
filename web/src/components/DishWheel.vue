<script setup lang="ts">
import { getCurrentInstance, onMounted, watch } from 'vue'

/** 转盘条目。 */
export interface WheelItem {
  id: string
  name: string
}

const props = withDefaults(
  defineProps<{
    items: WheelItem[]
    /** 直径（px）。不传时按屏宽自适应。 */
    size?: number
    /** 旋转动画时长（毫秒）。超过 2500ms 用户会不耐烦。 */
    duration?: number
  }>(),
  { size: 0, duration: 1800 },
)

const emit = defineEmits<{
  (e: 'done', index: number): void
}>()

const CANVAS_ID = 'fm-wheel'

/**
 * 扇区配色：深色底 + 霓虹倾向。
 * 深夜食堂的霓虹招牌就是这个路子 —— 底色压暗，让青色文字与描边发光。
 */
const SECTOR_COLORS = [
  '#0B4A55', '#5E0F3D', '#2C1560', '#0B5245',
  '#0A3F52', '#6B1230', '#3A1560', '#0B4A3D',
]

const NEON_CYAN = '#00F0FF'
const NEON_MAGENTA = '#FF2E97'
const VOID = '#05050A'
const SURFACE = '#0E0E18'

const instance = getCurrentInstance()

let rotation = 0
let timer: ReturnType<typeof setTimeout> | null = null
let spinning = false

const diameter = () => props.size || Math.round(uni.getSystemInfoSync().windowWidth * 0.72)

function context() {
  return uni.createCanvasContext(CANVAS_ID, instance?.proxy as never)
}

/** 截断菜名，避免长名字溢出扇区。 */
function shortName(name: string): string {
  return name.length <= 5 ? name : `${name.slice(0, 4)}…`
}

function draw() {
  const items = props.items
  if (items.length === 0) return

  const size = diameter()
  const center = size / 2
  const radius = center - 6
  const sector = (Math.PI * 2) / items.length

  const ctx = context()

  // ── 外圈光晕（两层，营造霓虹溢光）─────────────────────
  ctx.beginPath()
  ctx.arc(center, center, radius + 3, 0, Math.PI * 2)
  ctx.setStrokeStyle('rgba(0, 240, 255, 0.18)')
  ctx.setLineWidth(8)
  ctx.stroke()

  ctx.beginPath()
  ctx.arc(center, center, radius + 1, 0, Math.PI * 2)
  ctx.setStrokeStyle(NEON_CYAN)
  ctx.setLineWidth(2)
  ctx.stroke()

  // ── 扇区 ────────────────────────────────────────────────
  items.forEach((item, index) => {
    const start = index * sector + (rotation * Math.PI) / 180
    const end = start + sector

    ctx.beginPath()
    ctx.moveTo(center, center)
    ctx.arc(center, center, radius, start, end)
    ctx.closePath()
    ctx.setFillStyle(SECTOR_COLORS[index % SECTOR_COLORS.length])
    ctx.fill()

    // 扇区分隔线：细霓虹，让每个格子像独立的灯箱
    ctx.beginPath()
    ctx.moveTo(center, center)
    ctx.lineTo(
      center + Math.cos(start) * radius,
      center + Math.sin(start) * radius,
    )
    ctx.setStrokeStyle('rgba(0, 240, 255, 0.35)')
    ctx.setLineWidth(1)
    ctx.stroke()

    // 菜名：沿扇区中线排布，带发光
    const mid = start + sector / 2

    ctx.save()
    ctx.translate(center, center)
    ctx.rotate(mid)
    ctx.setFontSize(Math.max(11, Math.round(size / 26)))
    ctx.setTextAlign('right')
    ctx.setTextBaseline('middle')
    ctx.setShadow(0, 0, 8, NEON_CYAN)
    ctx.setFillStyle('#EAFDFF')
    ctx.fillText(shortName(item.name), radius - 14, 0)
    ctx.restore()
  })

  // ── 中心盘 ──────────────────────────────────────────────
  const hubRadius = Math.round(size * 0.14)

  ctx.beginPath()
  ctx.arc(center, center, hubRadius + 3, 0, Math.PI * 2)
  ctx.setFillStyle('rgba(0, 240, 255, 0.2)')
  ctx.fill()

  ctx.beginPath()
  ctx.arc(center, center, hubRadius, 0, Math.PI * 2)
  ctx.setFillStyle(SURFACE)
  ctx.fill()
  ctx.setStrokeStyle(NEON_CYAN)
  ctx.setLineWidth(2)
  ctx.stroke()

  ctx.setFontSize(Math.max(12, Math.round(size / 22)))
  ctx.setTextAlign('center')
  ctx.setTextBaseline('middle')
  ctx.setShadow(0, 0, 10, NEON_CYAN)
  ctx.setFillStyle(NEON_CYAN)
  ctx.fillText('吃啥', center, center)

  // ── 顶部指针 ────────────────────────────────────────────
  ctx.setShadow(0, 0, 12, NEON_MAGENTA)
  ctx.beginPath()
  ctx.moveTo(center, 2)
  ctx.lineTo(center - 10, 24)
  ctx.lineTo(center + 10, 24)
  ctx.closePath()
  ctx.setFillStyle(NEON_MAGENTA)
  ctx.fill()

  // 指针下的小圆点，像指示灯
  ctx.beginPath()
  ctx.arc(center, 30, 3, 0, Math.PI * 2)
  ctx.setFillStyle('#FFFFFF')
  ctx.fill()

  ctx.draw()
}

/** 让第 index 个扇区停在顶部指针处所需的角度。 */
function angleFor(index: number, turns: number): number {
  const sectorDeg = 360 / props.items.length
  // 指针在正上方（canvas 坐标系里是 -90°），把扇区中心转过去
  const target = 270 - (index * sectorDeg + sectorDeg / 2)
  return turns * 360 + target
}

/** 开始旋转，最终停在第 index 个扇区。 */
function spin(index: number) {
  if (spinning || props.items.length === 0) return

  spinning = true

  const from = rotation % 360
  const to = angleFor(index, 4) + Math.floor(from / 360) * 360
  const startAt = Date.now()
  const duration = props.duration

  const step = () => {
    const elapsed = Date.now() - startAt
    const t = Math.min(1, elapsed / duration)
    // easeOutCubic：先快后慢，符合转盘的手感
    const eased = 1 - Math.pow(1 - t, 3)

    rotation = from + (to - from) * eased
    draw()

    if (t < 1) {
      timer = setTimeout(step, 16)
      return
    }

    rotation = to % 360
    spinning = false
    draw()
    emit('done', index)
  }

  step()
}

/** 随机选一个扇区并转过去。 */
function spinRandom() {
  spin(Math.floor(Math.random() * props.items.length))
}

function stop() {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
  spinning = false
}

defineExpose({ spin, spinRandom, stop, redraw: draw })

watch(() => props.items, () => {
  rotation = 0
  draw()
}, { deep: true })

onMounted(() => {
  // 等布局稳定后再画，否则 H5 上可能拿到 0 尺寸
  setTimeout(draw, 50)
})
</script>

<template>
  <view class="wheel" :style="{ width: `${diameter()}px`, height: `${diameter()}px` }">
    <canvas
      :canvas-id="CANVAS_ID"
      :id="CANVAS_ID"
      class="wheel__canvas"
      :style="{ width: `${diameter()}px`, height: `${diameter()}px` }"
    />
  </view>
</template>

<style lang="scss" scoped>
.wheel {
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;

  /* 转盘背后的氛围光，让整块区域"亮"起来 */
  &::before {
    content: '';
    position: absolute;
    width: 88%;
    height: 88%;
    border-radius: 50%;
    background: radial-gradient(
      circle,
      rgba(0, 240, 255, 0.16) 0%,
      rgba(255, 46, 151, 0.08) 45%,
      transparent 70%
    );
    filter: blur(18px);
    pointer-events: none;
  }

  &__canvas {
    display: block;
    position: relative;
  }
}
</style>
