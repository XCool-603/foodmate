import { defineConfig } from 'vite'
import uni from '@dcloudio/vite-plugin-uni'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [uni()],

  server: {
    watch: {
      /**
       * 忽略编辑器 / 工具写入时产生的临时文件。
       *
       * 不少工具（含 AI 编码助手）采用「写临时文件 → 原子改名」的保存方式，
       * 会在源码目录里短暂出现 `.<name>.<pid>.<uuid>.tmpdir/` 这类目录。
       * Vite 的文件监听器一旦撞上它们就会抛 EBUSY 并**直接崩掉 dev server**：
       *
       *   Error: EBUSY: resource busy or locked, watch '.../.dish.ts.5732.xxxx.tmpdir/dish.ts.tmp'
       *
       * 这不是代码问题，但会让开发中途莫名中断，所以在这里显式排除。
       */
      ignored: ['**/.*.tmpdir/**', '**/*.tmp', '**/*.swp', '**/*~'],
    },
  },
})
