import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost } from '@/server/api'
import {
  adminFeatureFlagSchema,
  featureFlagOverrideSchema,
  featuresResponseSchema,
  upsertFeatureFlagOverrideSchema,
} from './feature-flags.schemas'
import { z } from 'zod'

export const fetchFeatureFlags = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/features', context)
    return featuresResponseSchema.parse(data)
  })

export const fetchAdminFeatureFlags = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/admin/feature-flags', context)
    return z.array(adminFeatureFlagSchema).parse(data)
  })

export const fetchFeatureFlagOverrides = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantId: z.string().uuid().optional(),
      userId: z.string().uuid().optional(),
    }),
  )
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams()
    if (data.tenantId) params.set('tenantId', data.tenantId)
    if (data.userId) params.set('userId', data.userId)
    const qs = params.size > 0 ? `?${params.toString()}` : ''
    const response = await apiGet(`/admin/feature-flags/overrides${qs}`, context)
    return z.array(featureFlagOverrideSchema).parse(response)
  })

export const upsertFeatureFlagOverride = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(upsertFeatureFlagOverrideSchema)
  .handler(async ({ context, data }) => {
    const response = await apiPost('/admin/feature-flags/overrides', context, data)
    return featureFlagOverrideSchema.parse(response)
  })

export const deleteFeatureFlagOverride = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/admin/feature-flags/overrides/${id}`, context)
  })
