import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { deviceDetailSchema, pagedDeviceExposuresSchema, pagedDevicesSchema } from './devices.schemas'
import { buildFilterParams } from './utils'

// Phase 1 canonical cleanup (Task 15): thin client for /api/devices.
// Replaces the legacy assets.functions.ts surface that targeted
// /api/assets (now deleted). Vulnerability/software detail sections
// are omitted until Phase 5 rewires those tables off Asset.

export const fetchDevices = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      criticality: z.string().optional(),
      businessLabelId: z.string().uuid().optional(),
      ownerType: z.string().optional(),
      deviceGroup: z.string().optional(),
      unassignedOnly: z.boolean().optional(),
      tenantId: z.string().optional(),
      search: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
      healthStatus: z.string().optional(),
      riskBand: z.string().optional(),
      tag: z.string().optional(),
      onboardingStatus: z.string().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/devices?${params.toString()}`, context)
    return pagedDevicesSchema.parse(data)
  })

export const fetchDeviceDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ deviceId: z.string() }))
  .handler(async ({ context, data: { deviceId } }) => {
    const data = await apiGet(`/devices/${deviceId}`, context)
    return deviceDetailSchema.parse(data)
  })

export const fetchDeviceExposures = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      deviceId: z.string(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: { deviceId, page, pageSize } }) => {
    const params = new URLSearchParams()
    if (page) params.set('page', String(page))
    if (pageSize) params.set('pageSize', String(pageSize))
    const suffix = params.size > 0 ? `?${params.toString()}` : ''
    const data = await apiGet(`/devices/${deviceId}/exposures${suffix}`, context)
    return pagedDeviceExposuresSchema.parse(data)
  })

export const assignDeviceOwner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      deviceId: z.string(),
      ownerType: z.enum(['User', 'Team']),
      ownerId: z.string(),
    }),
  )
  .handler(async ({ context, data: { deviceId, ownerType, ownerId } }) => {
    await apiPut(`/devices/${deviceId}/owner`, context, { ownerType, ownerId })
  })

export const assignDeviceSecurityProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      deviceId: z.string(),
      securityProfileId: z.string().nullable(),
    }),
  )
  .handler(async ({ context, data: { deviceId, securityProfileId } }) => {
    await apiPut(`/devices/${deviceId}/security-profile`, context, { securityProfileId })
  })

export const setDeviceCriticality = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ deviceId: z.string(), criticality: z.string() }))
  .handler(async ({ context, data: { deviceId, criticality } }) => {
    await apiPut(`/devices/${deviceId}/criticality`, context, { criticality })
  })

export const resetDeviceCriticalityOverride = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ deviceId: z.string() }))
  .handler(async ({ context, data: { deviceId } }) => {
    await apiPost(`/devices/${deviceId}/criticality/reset`, context, {})
  })

export const assignDeviceBusinessLabels = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      deviceId: z.string(),
      businessLabelIds: z.array(z.string().uuid()),
    }),
  )
  .handler(async ({ context, data: { deviceId, businessLabelIds } }) => {
    await apiPut(`/devices/${deviceId}/business-labels`, context, { businessLabelIds })
  })

export const bulkAssignDevices = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      deviceIds: z.array(z.string()),
      ownerType: z.enum(['User', 'Team']),
      ownerId: z.string(),
    }),
  )
  .handler(async ({ context, data }) => {
    await apiPost('/devices/bulk-assign', context, data)
  })
