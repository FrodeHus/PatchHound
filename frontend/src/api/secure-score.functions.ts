import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import { assetScoreDetailSchema, secureScoreSummarySchema } from './secure-score.schemas'

export const fetchSecureScoreSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/secure-score/summary', context)
    return secureScoreSummarySchema.parse(data)
  })

export const fetchAssetSecureScore = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ assetId: z.string().uuid() }))
  .handler(async ({ context, data: { assetId } }) => {
    const data = await apiGet(`/secure-score/assets/${assetId}`, context)
    return assetScoreDetailSchema.parse(data)
  })

export const fetchSecureScoreTarget = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/secure-score/target', context)
    return z.number().parse(data)
  })

export const updateSecureScoreTarget = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ targetScore: z.number().min(0).max(100) }))
  .handler(async ({ context, data }) => {
    await apiPut('/secure-score/target', context, data)
  })
