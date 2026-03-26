import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiPost } from '@/server/api'

export const revokeAllRemediations = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    await apiPost('/maintenance/revoke-all-remediations', context, {})
  })
