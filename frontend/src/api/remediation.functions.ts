import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { softwareRemediationContextSchema } from './remediation.schemas'

export const fetchRemediationContext = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ assetId: z.string().uuid() }))
  .handler(async ({ context, data: { assetId } }) => {
    const data = await apiGet(`/assets/${assetId}/remediation-context`, context)
    return softwareRemediationContextSchema.parse(data)
  })
