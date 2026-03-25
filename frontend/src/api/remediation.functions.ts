import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, type ApiRequestContext } from '@/server/api'
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

async function ensureRemediationWorkflowId(
  tenantSoftwareId: string,
  context: ApiRequestContext
) {
  const data = await apiPost(`/software/${tenantSoftwareId}/remediation/workflow`, context, {})
  return z.object({ workflowId: z.string().uuid() }).parse(data).workflowId
}

export const createDecision = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantSoftwareId: z.string().uuid(),
      workflowId: z.string().uuid().nullable().optional(),
      outcome: z.string(),
      justification: z.string().optional(),
      expiryDate: z.string().optional(),
      reEvaluationDate: z.string().optional(),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, workflowId, ...body } }) => {
    const resolvedWorkflowId = workflowId ?? (await ensureRemediationWorkflowId(tenantSoftwareId, context))
    const data = await apiPost(`/remediation/${resolvedWorkflowId}/decision`, context, body)
    return remediationDecisionSchema.parse(data)
  })

export const approveOrRejectDecision = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantSoftwareId: z.string().uuid(),
      workflowId: z.string().uuid().nullable().optional(),
      decisionId: z.string().uuid(),
      action: z.enum(['approve', 'reject', 'cancel']),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, workflowId, decisionId, action } }) => {
    const resolvedWorkflowId = workflowId ?? (await ensureRemediationWorkflowId(tenantSoftwareId, context))

    if (action === 'cancel') {
      await apiPost(`/remediation/${resolvedWorkflowId}/decision/${decisionId}/cancel`, context, {})
      return
    }

    await apiPost(`/remediation/${resolvedWorkflowId}/approval`, context, {
      action: action === 'reject' ? 'deny' : action,
    })
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
      workflowId: z.string().uuid().nullable().optional(),
      recommendedOutcome: z.string(),
      rationale: z.string(),
      priorityOverride: z.string().optional(),
      tenantVulnerabilityId: z.string().uuid().optional(),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, workflowId, ...body } }) => {
    const resolvedWorkflowId = workflowId ?? (await ensureRemediationWorkflowId(tenantSoftwareId, context))
    const data = await apiPost(`/remediation/${resolvedWorkflowId}/analysis`, context, body)
    return analystRecommendationSchema.parse(data)
  })

export const verifyRecurringRemediation = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      workflowId: z.string().uuid(),
      action: z.enum(['keepCurrentDecision', 'chooseNewDecision']),
    })
  )
  .handler(async ({ context, data: { workflowId, action } }) => {
    await apiPost(`/remediation/${workflowId}/verification`, context, { action })
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
