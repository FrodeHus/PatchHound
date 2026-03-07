import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import {
  pagedTenantSchema,
  tenantDetailSchema,
  tenantIngestionSourceSchema,
} from './settings.schemas'
import { z } from 'zod'

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
    const data = await apiGet(`/tenants?page=${page}&pageSize=${pageSize}`, context.token)
    return pagedTenantSchema.parse(data)
  })

export const fetchTenantDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantId: z.string() }))
  .handler(async ({ context, data: { tenantId } }) => {
    const data = await apiGet(`/tenants/${tenantId}`, context.token)
    return tenantDetailSchema.parse(data)
  })

export const updateTenant = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    tenantId: z.string(),
    name: z.string().min(1),
    ingestionSources: z.array(tenantIngestionSourceSchema),
  }))
  .handler(async ({ context, data: { tenantId, name, ingestionSources } }) => {
    await apiPut(`/tenants/${tenantId}`, context.token, { name, ingestionSources })
  })
