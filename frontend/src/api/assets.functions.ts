import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { assetDetailSchema, pagedAssetsSchema } from './assets.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'

export const fetchAssets = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetType: z.string().optional(),
      criticality: z.string().optional(),
      ownerType: z.string().optional(),
      tenantId: z.string().optional(),
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

export const fetchAssetDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ assetId: z.string() }))
  .handler(async ({ context, data: { assetId } }) => {
    const data = await apiGet(`/assets/${assetId}`, context.token)
    return assetDetailSchema.parse(data)
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

export const assignAssetSecurityProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetId: z.string(),
      securityProfileId: z.string().nullable(),
    }),
  )
  .handler(async ({ context, data: { assetId, securityProfileId } }) => {
    await apiPut(`/assets/${assetId}/security-profile`, context.token, { securityProfileId })
  })

export const setAssetCriticality = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ assetId: z.string(), criticality: z.string() }))
  .handler(async ({ context, data: { assetId, criticality } }) => {
    await apiPut(`/assets/${assetId}/criticality`, context.token, { criticality })
  })

export const bulkAssignAssets = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    assetIds: z.array(z.string()),
    ownerType: z.enum(['User', 'Team']),
    ownerId: z.string(),
  }))
  .handler(async ({ context, data }) => {
    await apiPost('/assets/bulk-assign', context.token, data)
  })
