import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiPost } from '@/server/api'
import { z } from 'zod'

const systemStatusSchema = z.object({
  openBaoAvailable: z.boolean(),
  openBaoInitialized: z.boolean(),
  openBaoSealed: z.boolean(),
})

export const unsealOpenBao = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      keys: z.array(z.string().min(1)).length(3),
    }),
  )
  .handler(async ({ context, data }) => {
    const response = await apiPost('/system/openbao/unseal', context.token, data)
    return systemStatusSchema.parse(response)
  })
