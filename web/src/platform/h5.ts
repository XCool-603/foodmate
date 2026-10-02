import { PlatformUnsupportedError } from './types'
import type { GeoPoint, IPlatformAdapter, LoginResult, ShareOptions } from './types'

/**
 * H5 适配器。
 *
 * H5 主要用于本地开发与内测预览：无小程序宿主，因此
 * 登录降级为游客模式（后端会按 `Platform.H5` 建号）。
 */
export const h5Adapter: IPlatformAdapter = {
  name: 'h5',
  label: '网页',
  supportsTimelineShare: false,

  login(): Promise<LoginResult> {
    // 游客模式：用本地持久化的设备 ID 作为登录凭证，后端按 H5 平台处理
    const STORAGE_KEY = 'fm_guest_id'
    let guestId = uni.getStorageSync(STORAGE_KEY) as string
    if (!guestId) {
      guestId = `guest-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
      uni.setStorageSync(STORAGE_KEY, guestId)
    }
    return Promise.resolve({ code: guestId })
  },

  getLocation(): Promise<GeoPoint> {
    return new Promise((resolve, reject) => {
      if (typeof navigator === 'undefined' || !navigator.geolocation) {
        reject(new PlatformUnsupportedError('定位', 'h5'))
        return
      }
      navigator.geolocation.getCurrentPosition(
        (pos) => resolve({ latitude: pos.coords.latitude, longitude: pos.coords.longitude }),
        (err) => reject(new Error(err.message || '定位失败')),
        { timeout: 8000 },
      )
    })
  },

  share(options: ShareOptions): void {
    const nav = typeof navigator === 'undefined' ? undefined : navigator
    if (nav?.share) {
      void nav.share({ title: options.title, url: options.path })
    }
  },

  vibrate(): void {
    const nav = typeof navigator === 'undefined' ? undefined : navigator
    nav?.vibrate?.(15)
  },
}
