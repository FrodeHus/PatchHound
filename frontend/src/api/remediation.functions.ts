import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import {
  decisionContextSchema,
  remediationDecisionSchema,
  analystRecommendationSchema,
  vulnerabilityOverrideSchema,
} from './remediation.schemas'

export const fetchDecisionContext = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ assetId: z.string().uuid() }))
  .handler(async ({ context, data: { assetId } }) => {
    const data = await apiGet(`/assets/${assetId}/decision-context`, context)
    return decisionContextSchema.parse(data)
  })

export const createDecision = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetId: z.string().uuid(),
      outcome: z.string(),
      justification: z.string().optional(),
      expiryDate: z.string().optional(),
      reEvaluationDate: z.string().optional(),
    })
  )
  .handler(async ({ context, data: { assetId, ...body } }) => {
    const data = await apiPost(`/assets/${assetId}/decisions`, context, body)
    return remediationDecisionSchema.parse(data)
  })

export const approveOrRejectDecision = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetId: z.string().uuid(),
      decisionId: z.string().uuid(),
      action: z.enum(['approve', 'reject', 'cancel']),
    })
  )
  .handler(async ({ context, data: { assetId, decisionId, action } }) => {
    await apiPut(`/assets/${assetId}/decisions/${decisionId}`, context, { action })
  })

export const addVulnerabilityOverride = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetId: z.string().uuid(),
      decisionId: z.string().uuid(),
      tenantVulnerabilityId: z.string().uuid(),
      outcome: z.string(),
      justification: z.string(),
    })
  )
  .handler(async ({ context, data: { assetId, decisionId, ...body } }) => {
    const data = await apiPost(
      `/assets/${assetId}/decisions/${decisionId}/overrides`,
      context,
      body
    )
    return vulnerabilityOverrideSchema.parse(data)
  })

export const addRecommendation = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      assetId: z.string().uuid(),
      recommendedOutcome: z.string(),
      rationale: z.string(),
      priorityOverride: z.string().optional(),
      tenantVulnerabilityId: z.string().uuid().optional(),
    })
  )
  .handler(async ({ context, data: { assetId, ...body } }) => {
    const data = await apiPost(`/assets/${assetId}/recommendations`, context, body)
    return analystRecommendationSchema.parse(data)
  })
