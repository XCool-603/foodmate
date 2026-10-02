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

/** 扇区配色：暖色系交替，保证相邻扇区可区分。 */
const SECTOR_COLORS = [
  '#FF8C5F', '#FFB088', '#FF6B35', '#FFA07A',
  '#FFC9A8', '#FF7F4D', '#FFD4BC', '#FF9466',
]

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
  const radius = center - 4
  const sector = (Math.PI * 2) / items.length

  const ctx = context()

  // 背板
  ctx.beginPath()
  ctx.arc(center, center, radius, 0, Math.PI * 2)
  ctx.setFillStyle('#FFFFFF')
  ctx.fill()

  // 扇区
  items.forEach((item, index) => {
    const start = index * sector + (rotation * Math.PI) / 180
    const end = start + sector

    ctx.beginPath()
    ctx.moveTo(center, center)
    ctx.arc(center, center, radius, start, end)
    ctx.closePath()
    ctx.setFillStyle(SECTOR_COLORS[index % SECTOR_COLORS.length])
    ctx.fill()

    // 菜名：沿扇区中线排布
    const mid = start + sector / 2

    ctx.save()
    ctx.translate(center, center)
    ctx.rotate(mid)
    ctx.setFillStyle('#FFFFFF')
    ctx.setFontSize(Math.max(11, Math.round(size / 26)))
    ctx.setTextAlign('right')
    ctx.setTextBaseline('middle')
    ctx.fillText(shortName(item.name), radius - 12, 0)
    ctx.restore()
  })

  // 中心圆
  ctx.beginPath()
  ctx.arc(center, center, Math.round(size * 0.13), 0, Math.PI * 2)
  ctx.setFillStyle('#FFFFFF')
  ctx.fill()
  ctx.setFillStyle('#FF6B35')
  ctx.setFontSize(Math.max(12, Math.round(size / 22)))
  ctx.setTextAlign('center')
  ctx.setTextBaseline('middle')
  ctx.fillText('吃啥', center, center)

  // 顶部指针
  ctx.beginPath()
  ctx.moveTo(center, 2)
  ctx.lineTo(center - 9, 22)
  ctx.lineTo(center + 9, 22)
  ctx.closePath()
  ctx.setFillStyle('#1F2329')
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

  &__canvas {
    display: block;
  }
}
</style>
