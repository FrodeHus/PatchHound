import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const remediationTaskSummarySchema = z.object({
  openTaskCount: z.number(),
  overdueTaskCount: z.number(),
  nearestDueDate: isoDateTimeSchema.nullable(),
})

export const remediationTaskListItemSchema = z.object({
  id: z.string().uuid(),
  softwareAssetId: z.string().uuid(),
  tenantSoftwareId: z.string().uuid().nullable(),
  softwareName: z.string(),
  softwareVendor: z.string().nullable(),
  ownerTeamId: z.string().uuid(),
  ownerTeamName: z.string(),
  affectedDeviceCount: z.number(),
  criticalDeviceCount: z.number(),
  highOrWorseDeviceCount: z.number(),
  highestDeviceCriticality: z.string(),
  dueDate: isoDateTimeSchema,
  maintenanceWindowDate: isoDateTimeSchema.nullable(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
  status: z.string(),
  deviceNames: z.array(z.string()),
  assetOwners: z.array(z.string()),
})

export const pagedRemediationTasksSchema = pagedResponseMetaSchema.extend({
  items: z.array(remediationTaskListItemSchema),
})

export const remediationTaskCreateResultSchema = z.object({
  createdCount: z.number(),
  eligibleCount: z.number(),
})

export const remediationTaskTeamStatusSchema = z.object({
  ownerTeamId: z.string().uuid(),
  ownerTeamName: z.string(),
  status: z.string(),
  dueDate: isoDateTimeSchema,
  maintenanceWindowDate: isoDateTimeSchema.nullable(),
  updatedAt: isoDateTimeSchema,
})

export type RemediationTaskSummary = z.infer<typeof remediationTaskSummarySchema>
export type RemediationTaskListItem = z.infer<typeof remediationTaskListItemSchema>
export type PagedRemediationTasks = z.infer<typeof pagedRemediationTasksSchema>
export type RemediationTaskCreateResult = z.infer<typeof remediationTaskCreateResultSchema>
export type RemediationTaskTeamStatus = z.infer<typeof remediationTaskTeamStatusSchema>
