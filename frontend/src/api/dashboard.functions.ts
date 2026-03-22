import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { burndownTrendSchema, dashboardFilterOptionsSchema, dashboardRiskChangeBriefSchema, dashboardSummarySchema, heatmapResponseSchema, ownerDashboardSummarySchema, trendDataSchema } from './dashboard.schemas'
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
  .inputValidator(z.object({ days: z.number().optional() }))
  .handler(async ({ context, data: { days } }) => {
    const qs = days && days > 1 ? `?days=${days}` : ''
    const data = await apiGet(`/dashboard/risk-changes${qs}`, context)
    return dashboardRiskChangeBriefSchema.parse(data)
  })

export const fetchDashboardBurndown = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(dashboardFilterSchema)
  .handler(async ({ context, data: filters }) => {
    const qs = buildDashboardParams(filters)
    const data = await apiGet(`/dashboard/burndown${qs}`, context)
    return burndownTrendSchema.parse(data)
  })

export const fetchDashboardFilterOptions = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/filter-options', context)
    return dashboardFilterOptionsSchema.parse(data)
  })

export const fetchOwnerDashboardSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/owner-summary', context)
    return ownerDashboardSummarySchema.parse(data)
  })

export const fetchDashboardHeatmap = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    groupBy: z.string().optional(),
    minAgeDays: z.number().optional(),
    platform: z.string().optional(),
    deviceGroup: z.string().optional(),
  }))
  .handler(async ({ context, data: { groupBy, ...filters } }) => {
    const params = new URLSearchParams()
    if (groupBy) params.set('groupBy', groupBy)
    if (filters.minAgeDays !== undefined) params.set('minAgeDays', String(filters.minAgeDays))
    if (filters.platform) params.set('platform', filters.platform)
    if (filters.deviceGroup) params.set('deviceGroup', filters.deviceGroup)
    const qs = params.toString()
    const data = await apiGet(`/dashboard/heatmap${qs ? `?${qs}` : ''}`, context)
    return heatmapResponseSchema.parse(data)
  })
