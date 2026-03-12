import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { dashboardRiskChangeBriefSchema, dashboardSummarySchema, trendDataSchema } from './dashboard.schemas'

export const fetchDashboardSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/summary', context)
    return dashboardSummarySchema.parse(data)
  })

export const fetchDashboardTrends = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/trends', context)
    return trendDataSchema.parse(data)
  })

export const fetchDashboardRiskChanges = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/risk-changes', context)
    return dashboardRiskChangeBriefSchema.parse(data)
  })
