import type { GeoPoint, IPlatformAdapter, LoginResult, ShareOptions } from './types'

/**
 * 支付宝小程序适配器。
 *
 * 差异点：
 * - 登录 provider 为 `alipay`
 * - 不支持分享到朋友圈
 * - 分享通过 `my.showSharePanel` 主动唤起
 */
export const alipayAdapter: IPlatformAdapter = {
  name: 'alipay',
  label: '支付宝',
  supportsTimelineShare: false,

  login(): Promise<LoginResult> {
    return new Promise((resolve, reject) => {
      // uni 的类型定义未覆盖支付宝的 provider 取值，这里显式放宽
      const options = {
        provider: 'alipay',
        success: (res: { code?: string }) => {
          if (res.code) {
            resolve({ code: res.code })
          } else {
            reject(new Error('支付宝登录未返回 code'))
          }
        },
        fail: (err: { errMsg?: string }) => reject(new Error(err?.errMsg ?? '支付宝登录失败')),
      } as unknown as UniApp.LoginOptions

      uni.login(options)
    })
  },

  getLocation(): Promise<GeoPoint> {
    return new Promise((resolve, reject) => {
      uni.getLocation({
        type: 'gcj02',
        success: (res) => resolve({ latitude: res.latitude, longitude: res.longitude }),
        fail: (err) => reject(new Error(err?.errMsg ?? '定位失败')),
      })
    })
  },

  share(options: ShareOptions): void {
    const my = (globalThis as Record<string, any>).my
    if (typeof my?.showSharePanel === 'function') {
      my.showSharePanel({
        title: options.title,
        content: options.title,
        url: options.path,
      })
    }
  },

  vibrate(): void {
    try {
      uni.vibrateShort()
    } catch {
      // 不支持时静默忽略
    }
  },
}
