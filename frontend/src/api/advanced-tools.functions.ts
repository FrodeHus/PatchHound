import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import {
  advancedToolAssetExecutionResultSchema,
  advancedToolCatalogSchema,
  advancedToolExecutionResultSchema,
} from './advanced-tools.schemas'

export const fetchAdvancedTools = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ assetType: z.string().optional() }))
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams()
    if (data.assetType) {
      params.set('assetType', data.assetType)
    }

    const path = params.size > 0 ? `/advanced-tools?${params.toString()}` : '/advanced-tools'
    const response = await apiGet(path, context)
    return advancedToolCatalogSchema.parse(response)
  })

export const createAdvancedTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string().min(1),
      description: z.string(),
      supportedAssetTypes: z.array(z.string()).min(1),
      kqlQuery: z.string().min(1),
      enabled: z.boolean(),
    }),
  )
  .handler(async ({ context, data }) => {
    const response = await apiPost('/advanced-tools', context, data)
    return advancedToolCatalogSchema.shape.tools.element.parse(response)
  })

export const updateAdvancedTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      supportedAssetTypes: z.array(z.string()).min(1),
      kqlQuery: z.string().min(1),
      enabled: z.boolean(),
    }),
  )
  .handler(async ({ context, data }) => {
    const { id, ...body } = data
    await apiPut(`/advanced-tools/${id}`, context, body)
  })

export const deleteAdvancedTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data }) => {
    await apiDelete(`/advanced-tools/${data.id}`, context)
  })

export const testAdvancedToolQuery = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      kqlQuery: z.string().min(1),
      sampleParameters: z.record(z.string(), z.string().nullable()),
    }),
  )
  .handler(async ({ context, data }) => {
    const response = await apiPost('/advanced-tools/test', context, data)
    return advancedToolExecutionResultSchema.parse(response)
  })

export const runAdvancedToolForAsset = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetId: z.string().uuid(),
      toolId: z.string().uuid().optional(),
      kqlQuery: z.string().optional(),
      useAllOpenVulnerabilities: z.boolean(),
      vulnerabilityIds: z.array(z.string().uuid()).optional(),
    }),
  )
  .handler(async ({ context, data }) => {
    const { assetId, ...body } = data
    const response = await apiPost(`/advanced-tools/assets/${assetId}/run`, context, body)
    return advancedToolAssetExecutionResultSchema.parse(response)
  })
