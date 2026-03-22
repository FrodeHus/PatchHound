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
      tenantVulnerabilityId: z.string().uuid(),
      externalId: z.string(),
      title: z.string(),
      severity: z.string(),
      affectedAssetCount: z.number(),
      changedAt: z.string(),
    })),
    resolved: z.array(z.object({
      tenantVulnerabilityId: z.string().uuid(),
      externalId: z.string(),
      title: z.string(),
      severity: z.string(),
      affectedAssetCount: z.number(),
      changedAt: z.string(),
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

export type DashboardFilterOptions = z.infer<typeof dashboardFilterOptionsSchema>
export type DeviceGroupVulnerability = z.infer<typeof dashboardSummarySchema>['vulnerabilitiesByDeviceGroup'][number]

export const heatmapRowSchema = z.object({
  label: z.string(),
  critical: z.number(),
  high: z.number(),
  medium: z.number(),
  low: z.number(),
})

export const heatmapResponseSchema = z.array(heatmapRowSchema)

export type HeatmapRow = z.infer<typeof heatmapRowSchema>
