import { z } from 'zod'

export const softwareRemediationThreatSchema = z.object({
  epssScore: z.number().nullable(),
  epssPercentile: z.number().nullable(),
  knownExploited: z.boolean(),
  publicExploit: z.boolean(),
  activeAlert: z.boolean(),
  hasRansomwareAssociation: z.boolean(),
})

export const softwareRemediationTaskSchema = z.object({
  id: z.string().uuid(),
  status: z.string(),
  justification: z.string().nullable(),
  dueDate: z.string(),
  createdAt: z.string(),
})

export const softwareRemediationRiskAcceptanceSchema = z.object({
  id: z.string().uuid(),
  status: z.string(),
  justification: z.string(),
  conditions: z.string().nullable(),
  expiryDate: z.string().nullable(),
  requestedAt: z.string(),
})

export const softwareRemediationVulnSchema = z.object({
  vulnerabilityDefinitionId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  vendorScore: z.number().nullable(),
  effectiveSeverity: z.string(),
  effectiveScore: z.number().nullable(),
  cvssVector: z.string().nullable(),
  matchMethod: z.string(),
  confidence: z.string(),
  evidence: z.string(),
  firstSeenAt: z.string(),
  resolvedAt: z.string().nullable(),
  threat: softwareRemediationThreatSchema.nullable(),
  remediationTask: softwareRemediationTaskSchema.nullable(),
  riskAcceptance: softwareRemediationRiskAcceptanceSchema.nullable(),
})

export const softwareRemediationSummarySchema = z.object({
  totalVulnerabilities: z.number(),
  criticalCount: z.number(),
  highCount: z.number(),
  mediumCount: z.number(),
  lowCount: z.number(),
  withKnownExploit: z.number(),
  withActiveAlert: z.number(),
  pendingRemediationTasks: z.number(),
  riskAcceptedCount: z.number(),
})

export const softwareRemediationContextSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  criticality: z.string(),
  summary: softwareRemediationSummarySchema,
  vulnerabilities: z.array(softwareRemediationVulnSchema),
})

export type SoftwareRemediationContext = z.infer<typeof softwareRemediationContextSchema>
export type SoftwareRemediationVuln = z.infer<typeof softwareRemediationVulnSchema>
export type SoftwareRemediationSummary = z.infer<typeof softwareRemediationSummarySchema>
export type SoftwareRemediationThreat = z.infer<typeof softwareRemediationThreatSchema>
export type SoftwareRemediationTask = z.infer<typeof softwareRemediationTaskSchema>
export type SoftwareRemediationRiskAcceptance = z.infer<typeof softwareRemediationRiskAcceptanceSchema>
