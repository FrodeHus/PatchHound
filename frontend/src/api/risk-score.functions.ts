import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import { deviceGroupRiskDetailSchema, riskScoreSummarySchema, softwareRiskDetailSchema } from './risk-score.schemas'

export const fetchRiskScoreSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/risk-score/summary', context)
    return riskScoreSummarySchema.parse(data)
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
  .inputValidator(z.object({ tenantSoftwareId: z.string() }))
  .handler(async ({ context, data: { tenantSoftwareId } }) => {
    const data = await apiGet(`/risk-score/software/${tenantSoftwareId}`, context)
    return softwareRiskDetailSchema.parse(data)
  })

export const recalculateRiskScores = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    await apiPost('/risk-score/recalculate', context)
    return null
  })
