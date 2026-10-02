import type { GeoPoint, IPlatformAdapter, LoginResult, ShareOptions } from './types'

/**
 * 微信小程序适配器。
 *
 * 差异点：
 * - 登录 provider 为 `weixin`
 * - 支持分享到朋友圈（`onShareTimeline`）
 * - 可用 `wx.showShareImageMenu` 分享图片
 */
export const wechatAdapter: IPlatformAdapter = {
  name: 'wechat',
  label: '微信',
  supportsTimelineShare: true,

  login(): Promise<LoginResult> {
    return new Promise((resolve, reject) => {
      uni.login({
        provider: 'weixin',
        success: (res) => {
          if (res.code) {
            resolve({ code: res.code })
          } else {
            reject(new Error('微信登录未返回 code'))
          }
        },
        fail: (err) => reject(new Error(err?.errMsg ?? '微信登录失败')),
      })
    })
  },

  getLocation(): Promise<GeoPoint> {
    return new Promise((resolve, reject) => {
      // gcj02 是国内坐标系，与地图/外卖场景一致
      uni.getLocation({
        type: 'gcj02',
        success: (res) => resolve({ latitude: res.latitude, longitude: res.longitude }),
        fail: (err) => reject(new Error(err?.errMsg ?? '定位失败')),
      })
    })
  },

  share(options: ShareOptions): void {
    // 微信小程序的分享以页面钩子 onShareAppMessage 为主，
    // 这里仅在需要主动唤起时兜底。
    const wx = (globalThis as Record<string, any>).wx
    if (typeof wx?.showShareMenu === 'function') {
      wx.showShareMenu({ withShareTicket: true, menus: ['shareAppMessage', 'shareTimeline'] })
    }
    void options
  },

  vibrate(): void {
    try {
      uni.vibrateShort({ type: 'light' })
    } catch {
      // 不支持时静默忽略
    }
  },
}
