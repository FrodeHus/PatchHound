import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { pagedAuditLogSchema } from './audit-log.schemas'
import { z } from 'zod'

export const fetchAuditLog = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .validator(
    z.object({
      entityType: z.string().optional(),
      action: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = new URLSearchParams()
    if (filters.entityType) params.set('entityType', filters.entityType)
    if (filters.action) params.set('action', filters.action)
    params.set('page', String(filters.page ?? 1))
    params.set('pageSize', String(filters.pageSize ?? 50))

    const data = await apiGet(`/audit-log?${params.toString()}`, context.token)
    return pagedAuditLogSchema.parse(data)
  })
