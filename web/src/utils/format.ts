/** 把「分」格式化为展示用金额。 */
export function formatYuan(cents: number | null | undefined): string {
  if (cents === null || cents === undefined) return '—'
  const yuan = cents / 100
  return `¥${Number.isInteger(yuan) ? yuan : yuan.toFixed(1)}`
}

/** 格式化价格区间。 */
export function formatPriceRange(min: number | null, max: number | null): string {
  if (min === null && max === null) return '价格未知'
  if (min !== null && max !== null) {
    return min === max ? formatYuan(min) : `${formatYuan(min)}–${formatYuan(max)}`
  }
  return formatYuan(min ?? max)
}

/** 把 ISO 时间格式化为「HH:mm」。 */
export function formatTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** 按本地时间返回时段问候语。 */
export function greeting(): string {
  const h = new Date().getHours()
  if (h >= 5 && h < 10) return '早上好'
  if (h >= 10 && h < 14) return '中午好'
  if (h >= 14 && h < 18) return '下午好'
  if (h >= 18 && h < 22) return '晚上好'
  return '夜深了'
}

/** 按本地时间返回当前餐次的提示语。 */
export function mealPrompt(): string {
  const h = new Date().getHours()
  if (h >= 5 && h < 10) return '该吃早饭了，今天想吃点什么？'
  if (h >= 10 && h < 15) return '该吃午饭了，今天想吃点什么？'
  if (h >= 15 && h < 21) return '该吃晚饭了，今天想吃点什么？'
  return '来点夜宵？'
}
