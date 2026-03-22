import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import {
  pagedVulnerabilitySchema,
  vulnerabilityDetailSchema,
  aiReportSchema,
  commentSchema,
} from './vulnerabilities.schemas'
import { pagedAuditLogSchema } from './audit-log.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'

export const fetchVulnerabilities = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      severity: z.string().optional(),
      status: z.string().optional(),
      source: z.string().optional(),
      search: z.string().optional(),
      recurrenceOnly: z.boolean().optional(),
      presentOnly: z.boolean().optional(),
      minAgeDays: z.number().optional(),
      publicExploitOnly: z.boolean().optional(),
      knownExploitedOnly: z.boolean().optional(),
      activeAlertOnly: z.boolean().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters, { pageSize: 25 })
    const data = await apiGet(`/vulnerabilities?${params.toString()}`, context)
    return pagedVulnerabilitySchema.parse(data)
  })

export const fetchVulnerabilityDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/vulnerabilities/${id}`, context)
    return vulnerabilityDetailSchema.parse(data)
  })

export const updateOrganizationalSeverity = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string(),
      adjustedSeverity: z.string(),
      justification: z.string(),
      assetCriticalityFactor: z.string().optional(),
      exposureFactor: z.string().optional(),
      compensatingControls: z.string().optional(),
    }),
  )
  .handler(async ({ context, data: { id, ...payload } }) => {
    await apiPut(`/vulnerabilities/${id}/organizational-severity`, context, payload)
  })

export const generateAiReport = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string(), tenantAiProfileId: z.string().uuid().optional() }))
  .handler(async ({ context, data: { id, tenantAiProfileId } }) => {
    const data = await apiPost(`/vulnerabilities/${id}/ai-report`, context, { tenantAiProfileId })
    return aiReportSchema.parse(data)
  })

export const fetchVulnerabilityComments = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/vulnerabilities/${id}/comments`, context)
    return z.array(commentSchema).parse(data)
  })

export const addVulnerabilityComment = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string(), content: z.string() }))
  .handler(async ({ context, data: { id, content } }) => {
    const data = await apiPost(`/vulnerabilities/${id}/comments`, context, { content })
    return commentSchema.parse(data)
  })

export const fetchVulnerabilityTimeline = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const params = new URLSearchParams({
      entityType: 'TenantVulnerability',
      entityId: id,
      page: '1',
      pageSize: '50',
    })
    const data = await apiGet(`/audit-log?${params.toString()}`, context)
    const parsed = pagedAuditLogSchema.parse(data)
    return parsed.items
  })
