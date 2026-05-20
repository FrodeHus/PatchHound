import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const enrichmentChangeValueSchema = z.union([
  z.string(),
  z.number(),
  z.boolean(),
  z.record(z.string(), z.unknown()),
  z.array(z.unknown()),
  z.null(),
])

export const enrichmentChangeSchema = z.object({
  id: z.string().uuid(),
  scope: z.enum(['Global', 'Tenant']),
  tenantId: z.string().uuid().nullable(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  sourceKey: z.string(),
  sourceDisplayName: z.string().nullable(),
  fieldPath: z.string(),
  displayName: z.string(),
  oldValue: enrichmentChangeValueSchema,
  newValue: enrichmentChangeValueSchema,
  valueKind: z.string(),
  changedAt: isoDateTimeSchema,
  enrichmentRunId: z.string().uuid().nullable(),
  enrichmentJobId: z.string().uuid().nullable(),
  changeReason: z.string().nullable(),
  confidence: z.number().nullable(),
})

export const pagedEnrichmentChangeSchema = pagedResponseMetaSchema.extend({
  items: z.array(enrichmentChangeSchema),
})

export type EnrichmentChange = z.infer<typeof enrichmentChangeSchema>
export type PagedEnrichmentChanges = z.infer<typeof pagedEnrichmentChangeSchema>
