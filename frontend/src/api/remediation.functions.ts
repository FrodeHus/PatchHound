import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import {
  analystRecommendationSchema,
  decisionContextSchema,
  pagedDecisionListSchema,
  remediationDecisionSchema,
  threatIntelSchema,
  vulnerabilityOverrideSchema,
} from './remediation.schemas'
import { buildFilterParams } from './utils'

export const fetchDecisionContext = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ caseId: z.string().uuid() }))
  .handler(async ({ context, data: { caseId } }) => {
    const data = await apiGet(`/remediation/cases/${caseId}/decision-context`, context)
    return decisionContextSchema.parse(data)
  })

export const fetchTenantSoftwareDecisionContext = createServerFn({ method: 'GET' })
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
      caseId: z.string().uuid(),
      outcome: z.string(),
      justification: z.string().optional(),
      maintenanceWindowDate: z.string().optional(),
      expiryDate: z.string().optional(),
      reEvaluationDate: z.string().optional(),
      deadlineMode: z.enum(['forever', 'date']).optional(),
    })
  )
  .handler(async ({ context, data: { caseId, ...body } }) => {
    const data = await apiPost(`/remediation/cases/${caseId}/decision`, context, body)
    return remediationDecisionSchema.parse(data)
  })

export const approveOrRejectDecision = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      caseId: z.string().uuid(),
      decisionId: z.string().uuid(),
      action: z.enum(['approve', 'reject', 'cancel']),
      justification: z.string().optional(),
      maintenanceWindowDate: z.string().optional(),
    })
  )
  .handler(async ({ context, data: { caseId, decisionId, action, justification, maintenanceWindowDate } }) => {
    if (action === 'cancel') {
      await apiPost(`/remediation/cases/${caseId}/decision/${decisionId}/cancel`, context, {})
      return
    }

    await apiPost(`/remediation/cases/${caseId}/approval`, context, {
      action: action === 'reject' ? 'deny' : action,
      justification: justification || undefined,
      maintenanceWindowDate: maintenanceWindowDate || undefined,
    })
  })

export const addVulnerabilityOverride = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      caseId: z.string().uuid(),
      decisionId: z.string().uuid(),
      vulnerabilityId: z.string().uuid(),
      outcome: z.string(),
      justification: z.string(),
    })
  )
  .handler(async ({ context, data: { caseId, decisionId, ...body } }) => {
    const data = await apiPost(
      `/remediation/cases/${caseId}/decisions/${decisionId}/overrides`,
      context,
      body
    )
    return vulnerabilityOverrideSchema.parse(data)
  })

export const addRecommendation = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      caseId: z.string().uuid(),
      recommendedOutcome: z.string(),
      rationale: z.string(),
      priorityOverride: z.string().optional(),
      vulnerabilityId: z.string().uuid().optional(),
    })
  )
  .handler(async ({ context, data: { caseId, ...body } }) => {
    const data = await apiPost(`/remediation/cases/${caseId}/analysis`, context, body)
    return analystRecommendationSchema.parse(data)
  })

export const verifyRecurringRemediation = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      caseId: z.string().uuid(),
      action: z.enum(['keepCurrentDecision', 'chooseNewDecision']),
    })
  )
  .handler(async ({ context, data: { caseId, action } }) => {
    await apiPost(`/remediation/cases/${caseId}/verification`, context, { action })
  })

export const generateThreatIntel = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ caseId: z.string().uuid() }))
  .handler(async ({ context, data: { caseId } }) => {
    const data = await apiPost(`/remediation/cases/${caseId}/threat-intel`, context, {})
    return threatIntelSchema.parse(data)
  })

export const fetchDecisionList = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      search: z.string().optional(),
      criticality: z.string().optional(),
      outcome: z.string().optional(),
      approvalStatus: z.string().optional(),
      decisionState: z.string().optional(),
      missedMaintenanceWindow: z.boolean().optional(),
      needsAnalystRecommendation: z.boolean().optional(),
      needsRemediationDecision: z.boolean().optional(),
      needsApproval: z.boolean().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    })
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters, { pageSize: 25 })
    const data = await apiGet(`/decisions?${params.toString()}`, context)
    return pagedDecisionListSchema.parse(data)
  })
