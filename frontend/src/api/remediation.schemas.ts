import { z } from 'zod'

export const vulnerabilityOverrideSchema = z.object({
  id: z.string().uuid(),
  tenantVulnerabilityId: z.string().uuid(),
  outcome: z.string(),
  justification: z.string(),
  createdAt: z.string(),
})

export const remediationDecisionSchema = z.object({
  id: z.string().uuid(),
  outcome: z.string(),
  approvalStatus: z.string(),
  justification: z.string(),
  decidedBy: z.string().uuid(),
  decidedAt: z.string(),
  approvedBy: z.string().uuid().nullable(),
  approvedAt: z.string().nullable(),
  expiryDate: z.string().nullable(),
  reEvaluationDate: z.string().nullable(),
  overrides: z.array(vulnerabilityOverrideSchema),
})

export const analystRecommendationSchema = z.object({
  id: z.string().uuid(),
  tenantVulnerabilityId: z.string().uuid().nullable(),
  recommendedOutcome: z.string(),
  rationale: z.string(),
  priorityOverride: z.string().nullable(),
  analystId: z.string().uuid(),
  createdAt: z.string(),
})

export const decisionVulnSchema = z.object({
  tenantVulnerabilityId: z.string().uuid(),
  vulnerabilityDefinitionId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  vendorScore: z.number().nullable(),
  effectiveSeverity: z.string().nullable(),
  effectiveScore: z.number().nullable(),
  cvssVector: z.string().nullable(),
  knownExploited: z.boolean(),
  publicExploit: z.boolean(),
  activeAlert: z.boolean(),
  epssScore: z.number().nullable(),
  episodeRiskScore: z.number().nullable(),
  overrideOutcome: z.string().nullable(),
})

export const decisionSummarySchema = z.object({
  totalVulnerabilities: z.number(),
  criticalCount: z.number(),
  highCount: z.number(),
  mediumCount: z.number(),
  lowCount: z.number(),
  withKnownExploit: z.number(),
  withActiveAlert: z.number(),
})

export const decisionRiskSchema = z.object({
  compositeScore: z.number(),
  riskBand: z.string(),
  assessedAt: z.string().nullable(),
})

export const decisionSlaSchema = z.object({
  criticalDays: z.number(),
  highDays: z.number(),
  mediumDays: z.number(),
  lowDays: z.number(),
  slaStatus: z.string(),
  dueDate: z.string().nullable(),
})

export const decisionContextSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  criticality: z.string(),
  summary: decisionSummarySchema,
  currentDecision: remediationDecisionSchema.nullable(),
  recommendations: z.array(analystRecommendationSchema),
  topVulnerabilities: z.array(decisionVulnSchema),
  riskScore: decisionRiskSchema.nullable(),
  sla: decisionSlaSchema.nullable(),
  aiNarrative: z.string().nullable(),
})

export type DecisionContext = z.infer<typeof decisionContextSchema>
export type RemediationDecision = z.infer<typeof remediationDecisionSchema>
export type AnalystRecommendation = z.infer<typeof analystRecommendationSchema>
export type DecisionVuln = z.infer<typeof decisionVulnSchema>
export type DecisionSummary = z.infer<typeof decisionSummarySchema>
export type DecisionRisk = z.infer<typeof decisionRiskSchema>
export type DecisionSla = z.infer<typeof decisionSlaSchema>
export type VulnerabilityOverride = z.infer<typeof vulnerabilityOverrideSchema>
