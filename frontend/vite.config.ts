/// <reference types="vitest" />
import { createLogger, defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import tsConfigPaths from 'vite-tsconfig-paths'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { nitro } from 'nitro/vite'

export default defineConfig(({ mode }) => {
  const isTest = mode === 'test' || process.env.VITEST === 'true'
  const viteLogger = createLogger()
  const ignoreThirdPartyDependencyWarnings = (
    warning: { code?: string; id?: string | null; exporter?: string | null },
    warn: (warning: unknown) => void,
  ) => {
    if (warning.code === 'MODULE_LEVEL_DIRECTIVE' && warning.id?.includes('/node_modules/')) {
      return
    }

    if (warning.code === 'UNUSED_EXTERNAL_IMPORT') {
      return
    }

    warn(warning)
  }
  const isIgnorableBuildWarningMessage = (message: string) =>
    message.includes('are imported from external module "@tanstack/')
    || message.includes('Module level directives cause errors when bundled, "use client"')

  return {
    server: { port: 3000 },
    customLogger: {
      ...viteLogger,
      warn(msg, options) {
        if (isIgnorableBuildWarningMessage(String(msg))) {
          return
        }

        viteLogger.warn(msg, options)
      },
    },
    plugins: [
      tsConfigPaths({ projects: ['./tsconfig.json'] }),
      ...(!isTest ? [tanstackStart()] : []),
      react(),
      tailwindcss(),
      ...(!isTest ? [nitro()] : []),
    ],
    build: {
      chunkSizeWarningLimit: 700,
      rollupOptions: {
        onwarn: ignoreThirdPartyDependencyWarnings,
      },
    },
    environments: {
      ssr: {
        build: {
          rollupOptions: {
            onwarn: ignoreThirdPartyDependencyWarnings,
          },
        },
      },
    },
    nitro: {
      rollupConfig: {
        onwarn: ignoreThirdPartyDependencyWarnings,
      },
    },
    test: {
      globals: true,
      environment: 'jsdom',
      setupFiles: ['./src/test-setup.ts'],
    },
  }
})
