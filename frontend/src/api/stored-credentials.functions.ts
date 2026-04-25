import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { createStoredCredentialSchema, storedCredentialSchema, updateStoredCredentialSchema } from './stored-credentials.schemas'
import { z } from 'zod'

export const fetchStoredCredentials = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    type: z.string().optional(),
    tenantId: z.string().uuid().optional(),
  }))
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams()
    if (data.type) params.set('type', data.type)
    if (data.tenantId) params.set('tenantId', data.tenantId)
    const qs = params.toString()
    const response = await apiGet(`/stored-credentials${qs ? `?${qs}` : ''}`, context)
    return z.array(storedCredentialSchema).parse(response)
  })

export const createStoredCredential = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(createStoredCredentialSchema)
  .handler(async ({ context, data }) => {
    const response = await apiPost('/stored-credentials', context, data)
    return storedCredentialSchema.parse(response)
  })

export const updateStoredCredential = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(updateStoredCredentialSchema)
  .handler(async ({ context, data: { id, ...payload } }) => {
    await apiPut(`/stored-credentials/${id}`, context, payload)
  })

export const deleteStoredCredential = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/stored-credentials/${id}`, context)
  })
