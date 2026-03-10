import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import {
  normalizedSoftwareDetailSchema,
  normalizedSoftwareVulnerabilitySchema,
  pagedNormalizedSoftwareInstallationsSchema,
} from './software.schemas'
import { buildFilterParams } from './utils'

export const fetchNormalizedSoftwareDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/software/${id}`, context)
    return normalizedSoftwareDetailSchema.parse(data)
  })

export const fetchNormalizedSoftwareInstallations = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string(),
      version: z.string().optional(),
      activeOnly: z.boolean().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const { id, ...query } = filters
    const params = buildFilterParams(query, { pageSize: 25 })
    const data = await apiGet(`/software/${id}/installations?${params.toString()}`, context)
    return pagedNormalizedSoftwareInstallationsSchema.parse(data)
  })

export const fetchNormalizedSoftwareVulnerabilities = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/software/${id}/vulnerabilities`, context)
    return z.array(normalizedSoftwareVulnerabilitySchema).parse(data)
  })
