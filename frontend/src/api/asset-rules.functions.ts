import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { buildFilterParams } from './utils'
import {
  assetRuleSchema,
  filterPreviewSchema,
  pagedAssetRulesSchema,
} from './asset-rules.schemas'
import type { AssetRule, FilterPreview } from './asset-rules.schemas'

type PagedAssetRules = z.infer<typeof pagedAssetRulesSchema>

export const fetchAssetRules = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }): Promise<PagedAssetRules> => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/asset-rules?${params.toString()}`, context)
    return pagedAssetRulesSchema.parse(data)
  })

export const fetchAssetRule = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }): Promise<AssetRule> => {
    const data = await apiGet(`/asset-rules/${id}`, context)
    return assetRuleSchema.parse(data)
  })

export const createAssetRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string(),
      description: z.string().optional(),
      filterDefinition: z.any(),
      operations: z.any(),
    }),
  )
  .handler(async ({ context, data }): Promise<AssetRule> => {
    const result = await apiPost('/asset-rules', context, data)
    return assetRuleSchema.parse(result)
  })

export const updateAssetRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string(),
      description: z.string().optional(),
      enabled: z.boolean(),
      filterDefinition: z.any(),
      operations: z.any(),
    }),
  )
  .handler(async ({ context, data: { id, ...payload } }): Promise<AssetRule> => {
    const result = await apiPut(`/asset-rules/${id}`, context, payload)
    return assetRuleSchema.parse(result)
  })

export const deleteAssetRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/asset-rules/${id}`, context)
  })

export const previewAssetRuleFilter = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ filterDefinition: z.any() }))
  .handler(async ({ context, data }): Promise<FilterPreview> => {
    const result = await apiPost('/asset-rules/preview', context, data)
    return filterPreviewSchema.parse(result)
  })

export const runAssetRules = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    await apiPost('/asset-rules/run', context)
  })

export const reorderAssetRules = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ ruleIds: z.array(z.string().uuid()) }))
  .handler(async ({ context, data }) => {
    await apiPut('/asset-rules/reorder', context, data)
  })
