<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

/**
 * 数字滚动。
 *
 * 分数、统计这类数字直接跳变会显得"死"，滚动过去才有"在计算"的感觉。
 * 用 easeOutCubic —— 起步快、落位慢，符合数字收敛的直觉。
 */
const props = withDefaults(
  defineProps<{
    value: number
    /** 动画时长（毫秒） */
    duration?: number
    /** 小数位 */
    decimals?: number
    prefix?: string
    suffix?: string
  }>(),
  { duration: 900, decimals: 0, prefix: '', suffix: '' },
)

const display = ref(0)
let timer: ReturnType<typeof setTimeout> | null = null

function animateTo(target: number) {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }

  const from = display.value
  const delta = target - from

  // 值没变就不用动画，避免重复触发时"跳一下"
  if (Math.abs(delta) < 0.001) {
    display.value = target
    return
  }

  const startAt = Date.now()
  const duration = Math.max(120, props.duration)

  const step = () => {
    const t = Math.min(1, (Date.now() - startAt) / duration)
    const eased = 1 - Math.pow(1 - t, 3)

    display.value = from + delta * eased

    if (t < 1) {
      timer = setTimeout(step, 16)
      return
    }

    display.value = target
    timer = null
  }

  step()
}

const text = computed(
  () => `${props.prefix}${display.value.toFixed(props.decimals)}${props.suffix}`,
)

onMounted(() => animateTo(props.value))

watch(() => props.value, (next) => animateTo(next))

onUnmounted(() => {
  if (timer) clearTimeout(timer)
})
</script>

<template>
  <text class="num">{{ text }}</text>
</template>

<style lang="scss" scoped>
.num {
  font-family: $cy-mono;
  font-variant-numeric: tabular-nums;
}
</style>
