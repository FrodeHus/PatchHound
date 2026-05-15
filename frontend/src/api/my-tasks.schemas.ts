import { z } from 'zod'

export const myTaskListItemSchema = z.object({
  remediationCaseId: z.string().uuid(),
  softwareName: z.string(),
  criticality: z.string(),
  outcome: z.string().nullable(),
  approvalStatus: z.string().nullable(),
  totalVulnerabilities: z.number(),
  criticalCount: z.number(),
  highCount: z.number(),
  riskScore: z.number().nullable(),
  riskBand: z.string().nullable(),
  slaStatus: z.string().nullable(),
  slaDueDate: z.string().nullable(),
  affectedDeviceCount: z.number(),
  softwareOwnerTeamName: z.string().nullable(),
  softwareOwnerAssignmentSource: z.string(),
  workflowStage: z.string().nullable(),
})

export const myTaskBucketSchema = z.object({
  bucket: z.enum(['recommendation', 'decision', 'approval']),
  items: z.array(myTaskListItemSchema),
  page: z.number(),
  pageSize: z.number(),
  hasMore: z.boolean(),
})

export const myTasksPageSchema = z.object({
  sections: z.array(myTaskBucketSchema),
})

export type MyTaskListItem = z.infer<typeof myTaskListItemSchema>
export type MyTaskBucket = z.infer<typeof myTaskBucketSchema>
export type MyTasksPageData = z.infer<typeof myTasksPageSchema>
