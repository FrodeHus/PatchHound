import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { buildFilterParams } from './utils'
import {
  deviceRuleSchema,
  filterPreviewSchema,
  pagedDeviceRulesSchema,
} from './device-rules.schemas'
import type { DeviceRule, FilterPreview } from './device-rules.schemas'

type PagedDeviceRules = z.infer<typeof pagedDeviceRulesSchema>

export const fetchDeviceRules = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }): Promise<PagedDeviceRules> => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/device-rules?${params.toString()}`, context)
    return pagedDeviceRulesSchema.parse(data)
  })

export const fetchDeviceRule = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }): Promise<DeviceRule> => {
    const data = await apiGet(`/device-rules/${id}`, context)
    return deviceRuleSchema.parse(data)
  })

export const createDeviceRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string(),
      description: z.string().optional(),
      filterDefinition: z.any(),
      operations: z.any(),
    }),
  )
  .handler(async ({ context, data }): Promise<DeviceRule> => {
    const result = await apiPost('/device-rules', context, data)
    return deviceRuleSchema.parse(result)
  })

export const updateDeviceRule = createServerFn({ method: 'POST' })
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
  .handler(async ({ context, data: { id, ...payload } }): Promise<DeviceRule> => {
    const result = await apiPut(`/device-rules/${id}`, context, payload)
    return deviceRuleSchema.parse(result)
  })

export const deleteDeviceRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/device-rules/${id}`, context)
  })

export const previewDeviceRuleFilter = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ filterDefinition: z.any() }))
  .handler(async ({ context, data }): Promise<FilterPreview> => {
    const result = await apiPost('/device-rules/preview', context, data)
    return filterPreviewSchema.parse(result)
  })

export const runDeviceRules = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    await apiPost('/device-rules/run', context)
  })

export const reorderDeviceRules = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ ruleIds: z.array(z.string().uuid()) }))
  .handler(async ({ context, data }) => {
    await apiPut('/device-rules/reorder', context, data)
  })
