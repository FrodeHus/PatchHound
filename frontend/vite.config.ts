/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import tsConfigPaths from 'vite-tsconfig-paths'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { nitro } from 'nitro/vite'

export default defineConfig(({ mode }) => {
  const isTest = mode === 'test' || process.env.VITEST === 'true'
  const ignoreThirdPartyModuleDirectiveWarnings = (warning: { code?: string; id?: string | null }, warn: (warning: unknown) => void) => {
    if (warning.code === 'MODULE_LEVEL_DIRECTIVE' && warning.id?.includes('/node_modules/')) {
      return
    }

    warn(warning)
  }

  return {
    server: { port: 3000 },
    plugins: [
      tsConfigPaths({ projects: ['./tsconfig.json'] }),
      ...(!isTest ? [tanstackStart()] : []),
      react(),
      tailwindcss(),
      ...(!isTest ? [nitro()] : []),
    ],
    build: {
      rollupOptions: {
        onwarn: ignoreThirdPartyModuleDirectiveWarnings,
      },
    },
    test: {
      globals: true,
      environment: 'jsdom',
      setupFiles: ['./src/test-setup.ts'],
    },
  }
})
