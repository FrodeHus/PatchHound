import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import {
  saveTenantAiProfileSchema,
  tenantAiProfileSchema,
  tenantAiProfileValidationSchema,
} from './ai-settings.schemas'
import { z } from 'zod'

export const fetchTenantAiProfiles = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/settings/ai', context)
    return z.array(tenantAiProfileSchema).parse(data)
  })

export const saveTenantAiProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(saveTenantAiProfileSchema)
  .handler(async ({ context, data }) => {
    const payload = { ...data }
    const response = data.id
      ? await apiPut(`/settings/ai/profiles/${data.id}`, context, payload)
      : await apiPost('/settings/ai/profiles', context, payload)
    return tenantAiProfileSchema.parse(response)
  })

export const validateTenantAiProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiPost(`/settings/ai/profiles/${id}/validate`, context)
    return tenantAiProfileValidationSchema.parse(data)
  })

export const setDefaultTenantAiProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiPost(`/settings/ai/profiles/${id}/set-default`, context)
    return tenantAiProfileSchema.parse(data)
  })
