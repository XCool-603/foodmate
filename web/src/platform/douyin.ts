import type { GeoPoint, IPlatformAdapter, LoginResult, ShareOptions } from './types'

/**
 * 抖音小程序适配器。
 *
 * 差异点：
 * - 登录 provider 为 `toutiao`
 * - 不支持分享到朋友圈
 * - 分享通过 `tt.showShareMenu` 配置菜单
 */
export const douyinAdapter: IPlatformAdapter = {
  name: 'douyin',
  label: '抖音',
  supportsTimelineShare: false,

  login(): Promise<LoginResult> {
    return new Promise((resolve, reject) => {
      // uni 的类型定义未覆盖抖音的 provider 取值，这里显式放宽
      const options = {
        provider: 'toutiao',
        success: (res: { code?: string }) => {
          if (res.code) {
            resolve({ code: res.code })
          } else {
            reject(new Error('抖音登录未返回 code'))
          }
        },
        fail: (err: { errMsg?: string }) => reject(new Error(err?.errMsg ?? '抖音登录失败')),
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
    const tt = (globalThis as Record<string, any>).tt
    if (typeof tt?.showShareMenu === 'function') {
      tt.showShareMenu({
        withShareTicket: true,
        success: () => undefined,
        fail: () => undefined,
      })
    }
    void options
  },

  vibrate(): void {
    try {
      uni.vibrateShort()
    } catch {
      // 不支持时静默忽略
    }
  },
}
