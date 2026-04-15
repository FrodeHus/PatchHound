import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import {
  tenantSoftwareDetailSchema,
  tenantSoftwareAiReportSchema,
  tenantSoftwareDescriptionJobSchema,
  tenantSoftwareVulnerabilitySchema,
  pagedTenantSoftwareSchema,
  pagedTenantSoftwareInstallationsSchema,
} from './software.schemas'
import { buildFilterParams } from './utils'

export const fetchTenantSoftwareDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/software/${id}`, context)
    return tenantSoftwareDetailSchema.parse(data)
  })

export const fetchTenantSoftware = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      search: z.string().optional(),
      category: z.string().optional(),
      vulnerableOnly: z.boolean().optional(),
      missedMaintenanceWindow: z.boolean().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters, { pageSize: 25 })
    const data = await apiGet(`/software?${params.toString()}`, context)
    return pagedTenantSoftwareSchema.parse(data)
  })

export const fetchTenantSoftwareInstallations = createServerFn({ method: 'GET' })
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
    return pagedTenantSoftwareInstallationsSchema.parse(data)
  })

export const fetchTenantSoftwareVulnerabilities = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/software/${id}/vulnerabilities`, context)
    return z.array(tenantSoftwareVulnerabilitySchema).parse(data)
  })

export const generateTenantSoftwareAiReport = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string(), tenantAiProfileId: z.string().uuid().optional() }))
  .handler(async ({ context, data: { id, tenantAiProfileId } }) => {
    const data = await apiPost(`/software/${id}/ai-report`, context, { tenantAiProfileId })
    return tenantSoftwareAiReportSchema.parse(data)
  })

export const generateTenantSoftwareDescription = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string(), tenantAiProfileId: z.string().uuid().optional() }))
  .handler(async ({ context, data: { id, tenantAiProfileId } }) => {
    const data = await apiPost(`/software/${id}/description`, context, { tenantAiProfileId })
    return tenantSoftwareDescriptionJobSchema.parse(data)
  })

export const fetchTenantSoftwareDescriptionStatus = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/software/${id}/description-status`, context)
    return z.nullable(tenantSoftwareDescriptionJobSchema).parse(data)
  })

export const fetchDeviceSoftware = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      deviceId: z.string(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: { deviceId, ...query } }) => {
    const params = buildFilterParams(query, { pageSize: 25 })
    const data = await apiGet(`/devices/${deviceId}/software?${params.toString()}`, context)
    return pagedTenantSoftwareInstallationsSchema.parse(data)
  })
