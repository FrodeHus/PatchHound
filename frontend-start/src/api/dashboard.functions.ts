import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { dashboardSummarySchema, trendDataSchema } from './dashboard.schemas'

export const fetchDashboardSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/summary', context.token)
    return dashboardSummarySchema.parse(data)
  })

export const fetchDashboardTrends = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/trends', context.token)
    return trendDataSchema.parse(data)
  })
