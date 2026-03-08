import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const auditLogSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  entityLabel: z.string().nullable(),
  action: z.string(),
  oldValues: z.string().nullable(),
  newValues: z.string().nullable(),
  userId: z.string().uuid(),
  userDisplayName: z.string().nullable(),
  timestamp: isoDateTimeSchema,
})

export const pagedAuditLogSchema = pagedResponseMetaSchema.extend({
  items: z.array(auditLogSchema),
})

export type AuditLogItem = z.infer<typeof auditLogSchema>
