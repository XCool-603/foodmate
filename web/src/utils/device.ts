const DEVICE_ID_KEY = 'fm_device_id'

/**
 * 取设备 ID（首次调用时生成并持久化）。
 *
 * M1 阶段用它代替登录 —— 后端按 `(Platform, OpenId)` 建号，
 * 与 M4 的真实登录共用同一张表，届时替换即可。
 */
export function getDeviceId(): string {
  try {
    let id = uni.getStorageSync(DEVICE_ID_KEY) as string

    if (!id) {
      id = `dev-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
      uni.setStorageSync(DEVICE_ID_KEY, id)
    }

    return id
  } catch {
    // Storage 不可用时退化为会话内临时 ID
    return `tmp-${Math.random().toString(36).slice(2, 10)}`
  }
}
