import type { IPlatformAdapter } from './types'

/**
 * 平台适配层入口。
 *
 * ⚠️ **整个项目里唯一出现条件编译的地方。**
 * 业务代码一律 `import { platform } from '@/platform'`，
 * 不得在页面或组件中写 `#ifdef`。
 *
 * uni-app 的 `#ifdef` 是编译期指令：未被选中的分支不会进入产物，
 * 因此各平台包中只包含自己那一份适配器实现。
 *
 * 注：打包器会对适配器对象做属性级 tree-shaking——尚未被业务代码调用的方法
 * （M0 阶段尚无登录/定位调用）不会出现在产物里。等对应功能接入后，
 * 被引用的方法会自动保留，属预期行为，不必排查。
 */

// ── 条件导入 ────────────────────────────────────────────
// 注意：类型检查器（vue-tsc）不理解 #ifdef，会看到全部分支；
// 因此这里用「先声明、后赋值」而不是每个分支各自 export const，
// 否则会报 "Cannot redeclare block-scoped variable"。

// #ifdef MP-WEIXIN
import { wechatAdapter } from './wechat'
// #endif

// #ifdef MP-ALIPAY
import { alipayAdapter } from './alipay'
// #endif

// #ifdef MP-TOUTIAO
import { douyinAdapter } from './douyin'
// #endif

// #ifdef H5
import { h5Adapter } from './h5'
// #endif

let adapter: IPlatformAdapter

// #ifdef MP-WEIXIN
adapter = wechatAdapter
// #endif

// #ifdef MP-ALIPAY
adapter = alipayAdapter
// #endif

// #ifdef MP-TOUTIAO
adapter = douyinAdapter
// #endif

// #ifdef H5
adapter = h5Adapter
// #endif

/** 当前平台的适配器实例。 */
export const platform: IPlatformAdapter = adapter

export * from './types'
