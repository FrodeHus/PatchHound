import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { businessLabelSchema, saveBusinessLabelSchema } from './business-labels.schemas'
import { withTenantOverride } from './utils'

export const fetchBusinessLabels = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantId: z.string().uuid().optional() }))
  .handler(async ({ context, data: input }) => {
    const response = await apiGet('/business-labels', withTenantOverride(context, input.tenantId))
    return z.array(businessLabelSchema).parse(response)
  })

export const createBusinessLabel = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(saveBusinessLabelSchema)
  .handler(async ({ context, data }) => {
    const response = await apiPost('/business-labels', context, data)
    return businessLabelSchema.parse(response)
  })

export const updateBusinessLabel = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(saveBusinessLabelSchema.extend({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id, ...payload } }) => {
    const response = await apiPut(`/business-labels/${id}`, context, payload)
    return businessLabelSchema.parse(response)
  })

export const deleteBusinessLabel = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/business-labels/${id}`, context)
  })
