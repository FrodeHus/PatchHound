import { z } from 'zod'

export const approvalAuditEntrySchema = z.object({
  action: z.string(),
  userDisplayName: z.string().nullable(),
  justification: z.string().nullable(),
  timestamp: z.string(),
})

export const approvalVulnSchema = z.object({
  vulnerabilityId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  vendorScore: z.number().nullable(),
  effectiveSeverity: z.string().nullable(),
  knownExploited: z.boolean(),
  epssScore: z.number().nullable(),
})

export const approvalDeviceSchema = z.object({
  deviceAssetId: z.string().uuid(),
  deviceName: z.string(),
  criticality: z.string(),
  version: z.string().nullable(),
  lastSeenAt: z.string(),
  openVulnerabilityCount: z.number(),
})

export const approvalDeviceVersionCohortSchema = z.object({
  version: z.string().nullable(),
  activeInstallCount: z.number(),
  deviceCount: z.number(),
  activeVulnerabilityCount: z.number(),
  firstSeenAt: z.string(),
  lastSeenAt: z.string(),
})

export const approvalRecommendationSchema = z.object({
  id: z.string().uuid(),
  recommendedOutcome: z.string(),
  rationale: z.string(),
  priorityOverride: z.string().nullable(),
  analystId: z.string().uuid(),
  createdAt: z.string(),
})

export const pagedVulnerabilityListSchema = z.object({
  items: z.array(approvalVulnSchema),
  totalCount: z.number(),
  page: z.number(),
  pageSize: z.number(),
})

export const pagedDeviceListSchema = z.object({
  items: z.array(approvalDeviceSchema),
  totalCount: z.number(),
  page: z.number(),
  pageSize: z.number(),
})

export const approvalTaskListItemSchema = z.object({
  id: z.string().uuid(),
  remediationCaseId: z.string().uuid(),
  type: z.string(),
  status: z.string(),
  softwareName: z.string(),
  criticality: z.string(),
  outcome: z.string(),
  highestSeverity: z.string(),
  vulnerabilityCount: z.number(),
  expiresAt: z.string(),
  maintenanceWindowDate: z.string().datetime({ offset: true }).nullable(),
  createdAt: z.string(),
  readAt: z.string().nullable(),
  decidedByName: z.string(),
  slaStatus: z.string().nullable(),
  slaDueDate: z.string().nullable(),
})

export const approvalTaskDetailSchema = z.object({
  id: z.string().uuid(),
  type: z.string(),
  status: z.string(),
  remediationDecisionId: z.string().uuid(),
  softwareName: z.string(),
  criticality: z.string(),
  outcome: z.string(),
  justification: z.string(),
  highestSeverity: z.string(),
  requiresJustification: z.boolean(),
  expiresAt: z.string(),
  maintenanceWindowDate: z.string().datetime({ offset: true }).nullable(),
  createdAt: z.string(),
  readAt: z.string().nullable(),
  decidedByName: z.string(),
  slaStatus: z.string().nullable(),
  slaDueDate: z.string().nullable(),
  riskScore: z.number().nullable(),
  riskBand: z.string().nullable(),
  vulnerabilities: pagedVulnerabilityListSchema,
  deviceVersionCohorts: z.array(approvalDeviceVersionCohortSchema),
  devices: pagedDeviceListSchema.nullable(),
  recommendations: z.array(approvalRecommendationSchema),
  auditTrail: z.array(approvalAuditEntrySchema),
})

export const pagedApprovalTaskListSchema = z.object({
  items: z.array(approvalTaskListItemSchema),
  totalCount: z.number(),
  page: z.number(),
  pageSize: z.number(),
  totalPages: z.number(),
})

export type ApprovalTaskListItem = z.infer<typeof approvalTaskListItemSchema>
export type ApprovalTaskDetail = z.infer<typeof approvalTaskDetailSchema>
export type PagedApprovalTaskList = z.infer<typeof pagedApprovalTaskListSchema>
export type ApprovalAuditEntry = z.infer<typeof approvalAuditEntrySchema>
export type ApprovalVuln = z.infer<typeof approvalVulnSchema>
export type ApprovalDevice = z.infer<typeof approvalDeviceSchema>
export type ApprovalDeviceVersionCohort = z.infer<typeof approvalDeviceVersionCohortSchema>
export type ApprovalRecommendation = z.infer<typeof approvalRecommendationSchema>
