import { useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const topVulnerabilitySchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  severity: z.string(),
  cvssScore: z.number().nullable(),
  affectedAssetCount: z.number(),
  daysSincePublished: z.number(),
})

const dashboardSummarySchema = z.object({
  exposureScore: z.number(),
  vulnerabilitiesBySeverity: z.record(z.string(), z.number()),
  vulnerabilitiesByStatus: z.record(z.string(), z.number()),
  slaCompliancePercent: z.number(),
  overdueTaskCount: z.number(),
  totalTaskCount: z.number(),
  averageRemediationDays: z.number(),
  topCriticalVulnerabilities: z.array(topVulnerabilitySchema),
})

const trendItemSchema = z.object({
  date: z.string(),
  severity: z.string(),
  count: z.number(),
})

const trendDataSchema = z.object({
  items: z.array(trendItemSchema),
})

export type DashboardSummary = z.infer<typeof dashboardSummarySchema>
export type TopVulnerability = z.infer<typeof topVulnerabilitySchema>
export type TrendData = z.infer<typeof trendDataSchema>
export type TrendItem = z.infer<typeof trendItemSchema>

export const dashboardKeys = {
  all: ['dashboard'] as const,
  summary: (tenantId?: string) => [...dashboardKeys.all, 'summary', tenantId ?? 'all'] as const,
  trends: (tenantId?: string) => [...dashboardKeys.all, 'trends', tenantId ?? 'all'] as const,
}

function withTenantQuery(path: string, tenantId?: string): string {
  if (!tenantId) {
    return path
  }

  const separator = path.includes('?') ? '&' : '?'
  return `${path}${separator}tenantId=${encodeURIComponent(tenantId)}`
}

export function useDashboardSummary(tenantId?: string) {
  return useQuery({
    queryKey: dashboardKeys.summary(tenantId),
    queryFn: async () => {
      const data = await apiClient.get<unknown>(withTenantQuery('/dashboard/summary', tenantId))
      return dashboardSummarySchema.parse(data)
    },
  })
}

export function useDashboardTrends(tenantId?: string) {
  return useQuery({
    queryKey: dashboardKeys.trends(tenantId),
    queryFn: async () => {
      const data = await apiClient.get<unknown>(withTenantQuery('/dashboard/trends', tenantId))
      return trendDataSchema.parse(data)
    },
  })
}
