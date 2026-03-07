import { z } from 'zod'

export const remediationTaskSchema = z.object({
  id: z.string().uuid(),
  vulnerabilityId: z.string().uuid(),
  assetId: z.string().uuid(),
  vulnerabilityTitle: z.string(),
  assetName: z.string(),
  status: z.string(),
  justification: z.string().nullable(),
  dueDate: z.string().datetime(),
  createdAt: z.string().datetime(),
  isOverdue: z.boolean(),
})

export const pagedTasksSchema = z.object({
  items: z.array(remediationTaskSchema),
  totalCount: z.number(),
})

export type RemediationTask = z.infer<typeof remediationTaskSchema>
