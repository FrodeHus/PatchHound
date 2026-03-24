import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import {
  decisionContextSchema,
  remediationDecisionSchema,
  analystRecommendationSchema,
  vulnerabilityOverrideSchema,
  pagedDecisionListSchema,
} from './remediation.schemas'
import { buildFilterParams } from './utils'

export const fetchDecisionContext = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantSoftwareId: z.string().uuid() }))
  .handler(async ({ context, data: { tenantSoftwareId } }) => {
    const data = await apiGet(`/software/${tenantSoftwareId}/remediation/decision-context`, context)
    return decisionContextSchema.parse(data)
  })

export const createDecision = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantSoftwareId: z.string().uuid(),
      outcome: z.string(),
      justification: z.string().optional(),
      expiryDate: z.string().optional(),
      reEvaluationDate: z.string().optional(),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, ...body } }) => {
    const data = await apiPost(`/software/${tenantSoftwareId}/remediation/decisions`, context, body)
    return remediationDecisionSchema.parse(data)
  })

export const approveOrRejectDecision = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantSoftwareId: z.string().uuid(),
      decisionId: z.string().uuid(),
      action: z.enum(['approve', 'reject', 'cancel']),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, decisionId, action } }) => {
    await apiPut(`/software/${tenantSoftwareId}/remediation/decisions/${decisionId}`, context, { action })
  })

export const addVulnerabilityOverride = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantSoftwareId: z.string().uuid(),
      decisionId: z.string().uuid(),
      tenantVulnerabilityId: z.string().uuid(),
      outcome: z.string(),
      justification: z.string(),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, decisionId, ...body } }) => {
    const data = await apiPost(
      `/software/${tenantSoftwareId}/remediation/decisions/${decisionId}/overrides`,
      context,
      body
    )
    return vulnerabilityOverrideSchema.parse(data)
  })

export const addRecommendation = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantSoftwareId: z.string().uuid(),
      recommendedOutcome: z.string(),
      rationale: z.string(),
      priorityOverride: z.string().optional(),
      tenantVulnerabilityId: z.string().uuid().optional(),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, ...body } }) => {
    const data = await apiPost(`/software/${tenantSoftwareId}/remediation/recommendations`, context, body)
    return analystRecommendationSchema.parse(data)
  })

export const fetchDecisionList = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      search: z.string().optional(),
      criticality: z.string().optional(),
      outcome: z.string().optional(),
      approvalStatus: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    })
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters, { pageSize: 25 })
    const data = await apiGet(`/decisions?${params.toString()}`, context)
    return pagedDecisionListSchema.parse(data)
  })
