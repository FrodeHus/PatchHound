import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { pagedAuditLogSchema } from './audit-log.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'

export const fetchAuditLog = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      entityType: z.string().optional(),
      entityId: z.string().uuid().optional(),
      action: z.string().optional(),
      tenantId: z.string().uuid().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/audit-log?${params.toString()}`, context)
    return pagedAuditLogSchema.parse(data)
  })
