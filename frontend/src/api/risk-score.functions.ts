import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import { deviceGroupRiskDetailSchema, riskScoreSummarySchema, softwareRiskDetailSchema } from './risk-score.schemas'

const riskScoreSummaryFiltersSchema = z.object({
  minAgeDays: z.number().optional(),
  platform: z.string().optional(),
  deviceGroup: z.string().optional(),
})

export const fetchRiskScoreSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(riskScoreSummaryFiltersSchema.optional())
  .handler(async ({ context, data: filters }) => {
    const params = new URLSearchParams()
    if (filters?.minAgeDays !== undefined) {
      params.set('minAgeDays', String(filters.minAgeDays))
    }
    if (filters?.platform) {
      params.set('platform', filters.platform)
    }
    if (filters?.deviceGroup) {
      params.set('deviceGroup', filters.deviceGroup)
    }
    const query = params.size > 0 ? `?${params.toString()}` : ''
    const response = await apiGet(`/risk-score/summary${query}`, context)
    return riskScoreSummarySchema.parse(response)
  })

export const fetchDeviceGroupRiskDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ deviceGroupName: z.string() }))
  .handler(async ({ context, data: { deviceGroupName } }) => {
    const data = await apiGet(`/risk-score/device-groups/${encodeURIComponent(deviceGroupName)}`, context)
    return deviceGroupRiskDetailSchema.parse(data)
  })

export const fetchSoftwareRiskDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ softwareProductId: z.string() }))
  .handler(async ({ context, data: { softwareProductId } }) => {
    const data = await apiGet(`/risk-score/software/${softwareProductId}`, context)
    return softwareRiskDetailSchema.parse(data)
  })

export const recalculateRiskScores = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    await apiPost('/risk-score/recalculate', context)
    return null
  })
