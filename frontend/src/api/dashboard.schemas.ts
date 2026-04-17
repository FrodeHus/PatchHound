import { z } from 'zod'

export const topVulnerabilitySchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  severity: z.string(),
  cvssScore: z.number().nullable(),
  affectedAssetCount: z.number(),
  daysSincePublished: z.number(),
})

export const unhandledVulnerabilitySchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  severity: z.string(),
  cvssScore: z.number().nullable(),
  affectedAssetCount: z.number(),
  daysSincePublished: z.number(),
  latestSeenAt: z.string().datetime({ offset: true }),
})

export const dashboardSummarySchema = z.object({
  exposureScore: z.number(),
  vulnerabilitiesBySeverity: z.record(z.string(), z.number()),
  vulnerabilitiesByStatus: z.record(z.string(), z.number()),
  slaCompliancePercent: z.number(),
  overdueTaskCount: z.number(),
  totalTaskCount: z.number(),
  averageRemediationDays: z.number(),
  topCriticalVulnerabilities: z.array(topVulnerabilitySchema),
  latestUnhandledVulnerabilities: z.array(unhandledVulnerabilitySchema),
  riskChangeBrief: z.object({
    appearedCount: z.number(),
    resolvedCount: z.number(),
    appeared: z.array(z.object({
      vulnerabilityId: z.string().uuid(),
      externalId: z.string(),
      title: z.string(),
      severity: z.string(),
      affectedAssetCount: z.number(),
      changedAt: z.string(),
      remediationCaseId: z.string().uuid().nullable().optional(),
    })),
    resolved: z.array(z.object({
      vulnerabilityId: z.string().uuid(),
      externalId: z.string(),
      title: z.string(),
      severity: z.string(),
      affectedAssetCount: z.number(),
      changedAt: z.string(),
      remediationCaseId: z.string().uuid().nullable().optional(),
    })),
    aiSummary: z.string().nullable(),
  }),
  recurringVulnerabilityCount: z.number(),
  recurrenceRatePercent: z.number(),
  topRecurringVulnerabilities: z.array(z.object({
    id: z.string().uuid(),
    externalId: z.string(),
    title: z.string(),
    episodeCount: z.number(),
    reappearanceCount: z.number(),
  })),
  topRecurringAssets: z.array(z.object({
    assetId: z.string().uuid(),
    name: z.string(),
    assetType: z.string(),
    recurringVulnerabilityCount: z.number(),
  })),
  vulnerabilitiesByDeviceGroup: z.array(z.object({
    deviceGroupName: z.string(),
    critical: z.number(),
    high: z.number(),
    medium: z.number(),
    low: z.number(),
    currentRiskScore: z.number().nullable().optional(),
    assetCount: z.number().nullable().optional(),
    openEpisodeCount: z.number().nullable().optional(),
  })),
  deviceHealthBreakdown: z.record(z.string(), z.number()),
  deviceOnboardingBreakdown: z.record(z.string(), z.number()),
  slaComplianceTrend: z.array(z.object({
    date: z.string(),
    percent: z.number(),
  })).optional(),
  metricSparklines: z.object({
    criticalBacklog: z.array(z.number()),
    overdueActions: z.array(z.number()),
    healthyTasks: z.array(z.number()),
    openStatuses: z.array(z.number()),
  }).optional(),
  vulnerabilityAgeBuckets: z.array(z.object({
    bucket: z.string(),
    count: z.number(),
    critical: z.number(),
    high: z.number(),
    medium: z.number(),
    low: z.number(),
  })).optional(),
  mttrBySeverity: z.array(z.object({
    severity: z.string(),
    days: z.number(),
    previousDays: z.number().nullable(),
  })).optional(),
})

export const trendItemSchema = z.object({
  date: z.string(),
  severity: z.string(),
  count: z.number(),
})

export const trendDataSchema = z.object({
  items: z.array(trendItemSchema),
})

export const dashboardRiskChangeBriefSchema = dashboardSummarySchema.shape.riskChangeBrief

export type DashboardSummary = z.infer<typeof dashboardSummarySchema>
export type TopVulnerability = z.infer<typeof topVulnerabilitySchema>
export type UnhandledVulnerability = z.infer<typeof unhandledVulnerabilitySchema>
export type TrendData = z.infer<typeof trendDataSchema>
export type TrendItem = z.infer<typeof trendItemSchema>
export type DashboardRiskChangeBrief = z.infer<typeof dashboardRiskChangeBriefSchema>

export const burndownTrendSchema = z.object({
  items: z.array(z.object({
    date: z.string(),
    discovered: z.number(),
    resolved: z.number(),
    netOpen: z.number(),
  })),
})

export type BurndownTrend = z.infer<typeof burndownTrendSchema>

export const dashboardFilterOptionsSchema = z.object({
  platforms: z.array(z.string()),
  deviceGroups: z.array(z.string()),
})

export const ownerAssetSummarySchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  deviceGroupName: z.string().nullable(),
  criticality: z.string(),
  currentRiskScore: z.number().nullable(),
  riskBand: z.string().nullable(),
  openEpisodeCount: z.number(),
  topDriverTitle: z.string().nullable(),
  topDriverSummary: z.string().nullable(),
  lastSeenAt: z.string().datetime({ offset: true }).nullable().optional(),
  criticalCount: z.number().optional().default(0),
  highCount: z.number().optional().default(0),
  mediumCount: z.number().optional().default(0),
  lowCount: z.number().optional().default(0),
})

export const ownerActionSchema = z.object({
  tenantSoftwareId: z.string().uuid(),
  vulnerabilityId: z.string().uuid(),
  taskId: z.string().uuid().nullable(),
  softwareName: z.string(),
  ownerTeamName: z.string(),
  externalId: z.string(),
  title: z.string(),
  softwareNames: z.array(z.string()),
  ownerSummary: z.string(),
  severity: z.string(),
  episodeRiskScore: z.number().nullable(),
  episodeRiskBand: z.string().nullable(),
  dueDate: z.string().datetime({ offset: true }).nullable(),
  actionState: z.string(),
})

export const ownerCloudAppActionSchema = z.object({
  cloudApplicationId: z.string().uuid(),
  appName: z.string(),
  appId: z.string().nullable(),
  ownerTeamName: z.string(),
  expiredCredentialCount: z.number(),
  expiringCredentialCount: z.number(),
  nearestExpiryAt: z.string().datetime({ offset: true }).nullable(),
})

export const ownerDashboardSummarySchema = z.object({
  ownedAssetCount: z.number(),
  assetsNeedingAttention: z.number(),
  openActionCount: z.number(),
  overdueActionCount: z.number(),
  topOwnedAssets: z.array(ownerAssetSummarySchema),
  actions: z.array(ownerActionSchema),
  cloudAppActions: z.array(ownerCloudAppActionSchema),
})

export type DashboardFilterOptions = z.infer<typeof dashboardFilterOptionsSchema>
export type DeviceGroupVulnerability = z.infer<typeof dashboardSummarySchema>['vulnerabilitiesByDeviceGroup'][number]
export type OwnerDashboardSummary = z.infer<typeof ownerDashboardSummarySchema>
export type OwnerAssetSummary = z.infer<typeof ownerAssetSummarySchema>
export type OwnerAction = z.infer<typeof ownerActionSchema>
export type OwnerCloudAppAction = z.infer<typeof ownerCloudAppActionSchema>

export const approvalAttentionTaskSchema = z.object({
  approvalTaskId: z.string().uuid(),
  remediationDecisionId: z.string().uuid(),
  remediationCaseId: z.string().uuid(),
  softwareName: z.string(),
  approvalType: z.string(),
  highestSeverity: z.string(),
  vulnerabilityCount: z.number(),
  expiresAt: z.string().datetime({ offset: true }),
  maintenanceWindowDate: z.string().datetime({ offset: true }).nullable(),
  createdAt: z.string().datetime({ offset: true }),
  attentionState: z.string(),
})

export const approvedPolicyDecisionSchema = z.object({
  decisionId: z.string().uuid(),
  remediationCaseId: z.string().uuid(),
  softwareName: z.string(),
  outcome: z.string(),
  justification: z.string().nullable(),
  highestSeverity: z.string(),
  vulnerabilityCount: z.number(),
  approvedAt: z.string().datetime({ offset: true }),
  expiryDate: z.string().datetime({ offset: true }).nullable(),
})

export const securityManagerDashboardSummarySchema = z.object({
  recentApprovedDecisions: z.array(approvedPolicyDecisionSchema),
  approvalTasksRequiringAttention: z.array(approvalAttentionTaskSchema),
})

export const approvedPatchingTaskSchema = z.object({
  patchingTaskId: z.string().uuid(),
  remediationDecisionId: z.string().uuid(),
  remediationCaseId: z.string().uuid(),
  softwareName: z.string(),
  ownerTeamName: z.string(),
  highestSeverity: z.string(),
  affectedDeviceCount: z.number(),
  approvedAt: z.string().datetime({ offset: true }),
  dueDate: z.string().datetime({ offset: true }),
  maintenanceWindowDate: z.string().datetime({ offset: true }).nullable(),
  status: z.string(),
})

export const devicePatchDriftSchema = z.object({
  deviceAssetId: z.string().uuid(),
  deviceName: z.string(),
  criticality: z.string(),
  highestSeverity: z.string(),
  oldVulnerabilityCount: z.number(),
  oldestPublishedDate: z.string().datetime({ offset: true }),
})

export const technicalManagerDashboardSummarySchema = z.object({
  missedMaintenanceWindowCount: z.number(),
  approvedPatchingTasks: z.array(approvedPatchingTaskSchema),
  devicesWithAgedVulnerabilities: z.array(devicePatchDriftSchema),
  approvalTasksRequiringAttention: z.array(approvalAttentionTaskSchema),
})

export type SecurityManagerDashboardSummary = z.infer<typeof securityManagerDashboardSummarySchema>
export type TechnicalManagerDashboardSummary = z.infer<typeof technicalManagerDashboardSummarySchema>
export type ApprovalAttentionTask = z.infer<typeof approvalAttentionTaskSchema>
export type ApprovedPolicyDecision = z.infer<typeof approvedPolicyDecisionSchema>
export type ApprovedPatchingTask = z.infer<typeof approvedPatchingTaskSchema>
export type DevicePatchDrift = z.infer<typeof devicePatchDriftSchema>

export const heatmapRowSchema = z.object({
  label: z.string(),
  critical: z.number(),
  high: z.number(),
  medium: z.number(),
  low: z.number(),
})

export const heatmapResponseSchema = z.array(heatmapRowSchema)

export type HeatmapRow = z.infer<typeof heatmapRowSchema>
