import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import {
  pagedTenantIngestionRunSchema,
  pagedTenantSchema,
  tenantDetailSchema,
} from './settings.schemas'
import { z } from 'zod'

const updateTenantIngestionSourceSchema = z.object({
  key: z.string(),
  displayName: z.string(),
  enabled: z.boolean(),
  syncSchedule: z.string(),
  credentials: z.object({
    clientId: z.string(),
    secret: z.string(),
    apiBaseUrl: z.string(),
    tokenScope: z.string(),
  }),
})

export const fetchTenants = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const page = filters.page ?? 1
    const pageSize = filters.pageSize ?? 100
    const data = await apiGet(`/tenants?page=${page}&pageSize=${pageSize}`, context)
    return pagedTenantSchema.parse(data)
  })

export const fetchTenantDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantId: z.string() }))
  .handler(async ({ context, data: { tenantId } }) => {
    const data = await apiGet(`/tenants/${tenantId}`, context)
    return tenantDetailSchema.parse(data)
  })

export const createTenant = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    name: z.string().min(1),
    entraTenantId: z.string().min(1),
  }))
  .handler(async ({ context, data: { name, entraTenantId } }) => {
    const data = await apiPost('/tenants', context, { name, entraTenantId })
    return tenantDetailSchema.parse(data)
  })

export const updateTenant = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    tenantId: z.string(),
    name: z.string().min(1),
    sla: z.object({
      criticalDays: z.number().int().positive(),
      highDays: z.number().int().positive(),
      mediumDays: z.number().int().positive(),
      lowDays: z.number().int().positive(),
    }),
    ingestionSources: z.array(updateTenantIngestionSourceSchema),
  }))
  .handler(async ({ context, data: { tenantId, name, sla, ingestionSources } }) => {
    await apiPut(`/tenants/${tenantId}`, context, { name, sla, ingestionSources })
  })

export const triggerTenantIngestionSync = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    tenantId: z.string(),
    sourceKey: z.string(),
  }))
  .handler(async ({ context, data: { tenantId, sourceKey } }) => {
    await apiPost(`/tenants/${tenantId}/ingestion-sources/${sourceKey}/sync`, context)
  })

export const fetchTenantIngestionRuns = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    tenantId: z.string(),
    sourceKey: z.string(),
    page: z.number().optional(),
    pageSize: z.number().optional(),
  }))
  .handler(async ({ context, data: { tenantId, sourceKey, page, pageSize } }) => {
    const queryPage = page ?? 1
    const queryPageSize = pageSize ?? 10
    const data = await apiGet(
      `/tenants/${tenantId}/ingestion-sources/${sourceKey}/runs?page=${queryPage}&pageSize=${queryPageSize}`,
      context,
    )
    return pagedTenantIngestionRunSchema.parse(data)
  })

export const deleteTenantIngestionRun = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    tenantId: z.string(),
    sourceKey: z.string(),
    runId: z.string().uuid(),
  }))
  .handler(async ({ context, data: { tenantId, sourceKey, runId } }) => {
    await apiDelete(`/tenants/${tenantId}/ingestion-sources/${sourceKey}/runs/${runId}`, context)
  })
