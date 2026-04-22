import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { buildFilterParams, withTenantOverride } from './utils'
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
      tenantId: z.string().uuid().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }): Promise<PagedDeviceRules> => {
    const requestContext = withTenantOverride(context, filters.tenantId)
    const params = buildFilterParams(filters)
    const data = await apiGet(`/device-rules?${params.toString()}`, requestContext)
    return pagedDeviceRulesSchema.parse(data)
  })

export const fetchDeviceRule = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid(), tenantId: z.string().uuid().optional() }))
  .handler(async ({ context, data: { id, tenantId } }): Promise<DeviceRule> => {
    const data = await apiGet(`/device-rules/${id}`, withTenantOverride(context, tenantId))
    return deviceRuleSchema.parse(data)
  })

export const createDeviceRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantId: z.string().uuid().optional(),
      name: z.string(),
      description: z.string().optional(),
      filterDefinition: z.any(),
      operations: z.any(),
    }),
  )
  .handler(async ({ context, data }): Promise<DeviceRule> => {
    const result = await apiPost('/device-rules', withTenantOverride(context, data.tenantId), data)
    return deviceRuleSchema.parse(result)
  })

export const updateDeviceRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantId: z.string().uuid().optional(),
      id: z.string().uuid(),
      name: z.string(),
      description: z.string().optional(),
      enabled: z.boolean(),
      filterDefinition: z.any(),
      operations: z.any(),
    }),
  )
  .handler(async ({ context, data: { id, ...payload } }): Promise<DeviceRule> => {
    const result = await apiPut(
      `/device-rules/${id}`,
      withTenantOverride(context, payload.tenantId),
      payload,
    )
    return deviceRuleSchema.parse(result)
  })

export const deleteDeviceRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid(), tenantId: z.string().uuid().optional() }))
  .handler(async ({ context, data: { id, tenantId } }) => {
    await apiDelete(`/device-rules/${id}`, withTenantOverride(context, tenantId))
  })

export const previewDeviceRuleFilter = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantId: z.string().uuid().optional(), filterDefinition: z.any() }))
  .handler(async ({ context, data }): Promise<FilterPreview> => {
    const result = await apiPost('/device-rules/preview', withTenantOverride(context, data.tenantId), data)
    return filterPreviewSchema.parse(result)
  })

export const runDeviceRules = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantId: z.string().uuid().optional() }).optional())
  .handler(async ({ context, data }) => {
    await apiPost('/device-rules/run', withTenantOverride(context, data?.tenantId))
  })

export const reorderDeviceRules = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantId: z.string().uuid().optional(), ruleIds: z.array(z.string().uuid()) }))
  .handler(async ({ context, data }) => {
    await apiPut('/device-rules/reorder', withTenantOverride(context, data.tenantId), data)
  })
