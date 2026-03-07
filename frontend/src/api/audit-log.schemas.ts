import { z } from 'zod'

export const auditLogSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  action: z.string(),
  oldValues: z.string().nullable(),
  newValues: z.string().nullable(),
  userId: z.string().uuid(),
  timestamp: z.string().datetime(),
})

export const pagedAuditLogSchema = z.object({
  items: z.array(auditLogSchema),
  totalCount: z.number(),
})

export type AuditLogItem = z.infer<typeof auditLogSchema>
