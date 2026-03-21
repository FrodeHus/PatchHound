import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const remediationTaskSchema = z.object({
  id: z.string().uuid(),
  tenantVulnerabilityId: z.string().uuid(),
  assetId: z.string().uuid(),
  vulnerabilityTitle: z.string(),
  assetName: z.string(),
  status: z.string(),
  episodeRiskScore: z.number().nullable(),
  episodeRiskBand: z.string().nullable(),
  justification: z.string().nullable(),
  dueDate: z.string().datetime(),
  createdAt: z.string().datetime(),
  isOverdue: z.boolean(),
})

export const pagedTasksSchema = pagedResponseMetaSchema.extend({
  items: z.array(remediationTaskSchema),
})

export type RemediationTask = z.infer<typeof remediationTaskSchema>
