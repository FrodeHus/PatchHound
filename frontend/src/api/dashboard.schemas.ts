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

export const dashboardSummarySchema = z.object({
  exposureScore: z.number(),
  vulnerabilitiesBySeverity: z.record(z.string(), z.number()),
  vulnerabilitiesByStatus: z.record(z.string(), z.number()),
  slaCompliancePercent: z.number(),
  overdueTaskCount: z.number(),
  totalTaskCount: z.number(),
  averageRemediationDays: z.number(),
  topCriticalVulnerabilities: z.array(topVulnerabilitySchema),
})

export const trendItemSchema = z.object({
  date: z.string(),
  severity: z.string(),
  count: z.number(),
})

export const trendDataSchema = z.object({
  items: z.array(trendItemSchema),
})

export type DashboardSummary = z.infer<typeof dashboardSummarySchema>
export type TopVulnerability = z.infer<typeof topVulnerabilitySchema>
export type TrendData = z.infer<typeof trendDataSchema>
export type TrendItem = z.infer<typeof trendItemSchema>
