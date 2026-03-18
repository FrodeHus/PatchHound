import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { dashboardFilterOptionsSchema, dashboardRiskChangeBriefSchema, dashboardSummarySchema, trendDataSchema } from './dashboard.schemas'
import { z } from 'zod'

const dashboardFilterSchema = z.object({
  minAgeDays: z.number().optional(),
  platform: z.string().optional(),
  deviceGroup: z.string().optional(),
})

function buildDashboardParams(
  filters: z.infer<typeof dashboardFilterSchema>,
): string {
  const params = new URLSearchParams()
  if (filters.minAgeDays !== undefined) {
    params.set('minAgeDays', String(filters.minAgeDays))
  }
  if (filters.platform !== undefined && filters.platform !== '') {
    params.set('platform', filters.platform)
  }
  if (filters.deviceGroup !== undefined && filters.deviceGroup !== '') {
    params.set('deviceGroup', filters.deviceGroup)
  }
  const qs = params.toString()
  return qs ? `?${qs}` : ''
}

export const fetchDashboardSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(dashboardFilterSchema)
  .handler(async ({ context, data: filters }) => {
    const qs = buildDashboardParams(filters)
    const data = await apiGet(`/dashboard/summary${qs}`, context)
    return dashboardSummarySchema.parse(data)
  })

export const fetchDashboardTrends = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(dashboardFilterSchema)
  .handler(async ({ context, data: filters }) => {
    const qs = buildDashboardParams(filters)
    const data = await apiGet(`/dashboard/trends${qs}`, context)
    return trendDataSchema.parse(data)
  })

export const fetchDashboardRiskChanges = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/risk-changes', context)
    return dashboardRiskChangeBriefSchema.parse(data)
  })

export const fetchDashboardFilterOptions = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/filter-options', context)
    return dashboardFilterOptionsSchema.parse(data)
  })
