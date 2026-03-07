import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import { pagedAssetsSchema } from './assets.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'

export const fetchAssets = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetType: z.string().optional(),
      ownerType: z.string().optional(),
      search: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/assets?${params.toString()}`, context.token)
    return pagedAssetsSchema.parse(data)
  })

export const assignAssetOwner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetId: z.string(),
      ownerType: z.enum(['User', 'Team']),
      ownerId: z.string(),
    }),
  )
  .handler(async ({ context, data: { assetId, ownerType, ownerId } }) => {
    await apiPut(`/assets/${assetId}/owner`, context.token, { ownerType, ownerId })
  })

export const setAssetCriticality = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ assetId: z.string(), criticality: z.string() }))
  .handler(async ({ context, data: { assetId, criticality } }) => {
    await apiPut(`/assets/${assetId}/criticality`, context.token, { criticality })
  })
