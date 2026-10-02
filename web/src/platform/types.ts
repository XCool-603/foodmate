/**
 * 多端适配层的统一接口。
 *
 * 设计原则：业务代码**零 `#ifdef`**。所有平台差异都收敛在 `platform/` 目录下，
 * 由 `platform/index.ts` 在编译期选择具体实现。
 */

/** 运行平台标识。 */
export type PlatformName = 'wechat' | 'alipay' | 'douyin' | 'h5'

/** 登录结果。 */
export interface LoginResult {
  /** 各端登录凭证，交给后端换取 openId。 */
  code: string
  /** 部分平台可直接拿到昵称，否则为 null。 */
  nickname?: string | null
  /** 头像地址。 */
  avatarUrl?: string | null
}

/** 经纬度坐标。 */
export interface GeoPoint {
  latitude: number
  longitude: number
}

/** 分享参数。 */
export interface ShareOptions {
  title: string
  /** 分享后打开的页面路径，如 `/pages/decide/index`。 */
  path: string
  imageUrl?: string
}

/** 平台适配器。每个平台实现一份。 */
export interface IPlatformAdapter {
  /** 平台标识。 */
  readonly name: PlatformName

  /** 平台中文名，用于展示。 */
  readonly label: string

  /** 是否支持分享到朋友圈 / 社交圈。 */
  readonly supportsTimelineShare: boolean

  /**
   * 登录，拿到平台凭证 code。
   * 失败时 reject 一个带可读 message 的 Error。
   */
  login(): Promise<LoginResult>

  /**
   * 请求定位权限并获取坐标。
   * 用户拒绝授权时 reject——调用方应静默降级，不要重复弹窗。
   */
  getLocation(): Promise<GeoPoint>

  /** 主动触发分享。小程序内多数场景由页面 `onShareAppMessage` 钩子被动处理。 */
  share(options: ShareOptions): void

  /** 轻震动反馈。不支持时静默忽略。 */
  vibrate(): void
}

/** 平台能力不可用（如 H5 无原生登录）。 */
export class PlatformUnsupportedError extends Error {
  constructor(operation: string, platform: PlatformName) {
    super(`当前平台（${platform}）不支持「${operation}」`)
    this.name = 'PlatformUnsupportedError'
  }
}
