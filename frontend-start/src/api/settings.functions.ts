import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import { pagedTenantSchema } from './settings.schemas'
import { z } from 'zod'

export const fetchTenants = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .validator(
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

export const updateTenantSettings = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .validator(z.object({ tenantId: z.string(), settings: z.string() }))
  .handler(async ({ context, data: { tenantId, settings } }) => {
    await apiPut(`/tenants/${tenantId}/settings`, context.token, { settings })
  })
