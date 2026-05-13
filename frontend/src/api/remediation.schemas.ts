import { z } from 'zod'

export const vulnerabilityOverrideSchema = z.object({
  id: z.string().uuid(),
  vulnerabilityId: z.string().uuid(),
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
  maintenanceWindowDate: z.string().nullable(),
  expiryDate: z.string().nullable(),
  reEvaluationDate: z.string().nullable(),
  latestRejection: z.object({
    comment: z.string().nullable(),
    rejectedAt: z.string().nullable(),
  }).nullable(),
  overrides: z.array(vulnerabilityOverrideSchema),
})

export const analystRecommendationSchema = z.object({
  id: z.string().uuid(),
  vulnerabilityId: z.string().uuid().nullable(),
  recommendedOutcome: z.string(),
  rationale: z.string(),
  priorityOverride: z.string().nullable(),
  analystId: z.string().uuid(),
  analystDisplayName: z.string().nullable(),
  createdAt: z.string(),
})

export const decisionVulnSchema = z.object({
  vulnerabilityId: z.string().uuid(),
  vulnerabilityDefinitionId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  description: z.string().nullable(),
  vendorSeverity: z.string(),
  vendorScore: z.number().nullable(),
  effectiveSeverity: z.string().nullable(),
  effectiveScore: z.number().nullable(),
  cvssVector: z.string().nullable(),
  firstSeenAt: z.string().nullable(),
  affectedDeviceCount: z.number(),
  affectedVersionCount: z.number(),
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

export const decisionWorkflowSummarySchema = z.object({
  affectedDeviceCount: z.number(),
  affectedOwnerTeamCount: z.number(),
  openPatchingTaskCount: z.number(),
  completedPatchingTaskCount: z.number(),
  openEpisodeTrend: z.array(z.object({
    day: z.string(),
    openEpisodeCount: z.number(),
  })),
})

export const decisionWorkflowStageSchema = z.object({
  id: z.string(),
  label: z.string(),
  state: z.string(),
  description: z.string(),
})

export const decisionWorkflowStateSchema = z.object({
  workflowId: z.string().uuid().nullable(),
  currentStage: z.string(),
  currentStageLabel: z.string(),
  currentStageDescription: z.string(),
  currentActorSummary: z.string(),
  canActOnCurrentStage: z.boolean(),
  currentUserRoles: z.array(z.string()),
  currentUserTeams: z.array(z.string()),
  expectedRoles: z.array(z.string()),
  expectedTeamName: z.string().nullable(),
  isInExpectedTeam: z.boolean().nullable(),
  isRecurrence: z.boolean(),
  hasActiveWorkflow: z.boolean(),
  stages: z.array(decisionWorkflowStageSchema),
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

export const decisionBusinessLabelSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  color: z.string().nullable(),
  weightCategory: z.string(),
  riskWeight: z.number(),
  affectedDeviceCount: z.number(),
})

export const decisionApprovalResolutionSchema = z.object({
  status: z.string(),
  justification: z.string().nullable(),
  resolvedAt: z.string().nullable(),
  resolvedByDisplayName: z.string().nullable(),
})

export const threatIntelSchema = z.object({
  summary: z.string().nullable(),
  generatedAt: z.string().nullable(),
  profileName: z.string().nullable(),
  canGenerate: z.boolean(),
  unavailableMessage: z.string().nullable(),
})

export const patchAssessmentSchema = z.object({
  recommendation: z.string().nullable(),
  confidence: z.string().nullable(),
  summary: z.string().nullable(),
  urgencyTier: z.string().nullable(),
  urgencyTargetSla: z.string().nullable(),
  urgencyReason: z.string().nullable(),
  similarVulnerabilities: z.string().nullable(),
  compensatingControlsUntilPatched: z.string().nullable(),
  references: z.string().nullable(),
  aiProfileName: z.string().nullable(),
  assessedAt: z.string().nullable(),
  jobStatus: z.string(),
})

export const decisionContextSchema = z.object({
  remediationCaseId: z.string().uuid(),
  tenantSoftwareId: z.string().uuid().nullable(),
  softwareName: z.string(),
  softwareVendor: z.string().nullable(),
  softwareCategory: z.string().nullable(),
  softwareDescription: z.string().nullable(),
  softwareOwnerTeamId: z.string().uuid().nullable(),
  softwareOwnerTeamName: z.string().nullable(),
  softwareOwnerAssignmentSource: z.string(),
  criticality: z.string(),
  businessLabels: z.array(decisionBusinessLabelSchema),
  summary: decisionSummarySchema,
  workflow: decisionWorkflowSummarySchema,
  workflowState: decisionWorkflowStateSchema,
  currentDecision: remediationDecisionSchema.nullable(),
  previousDecision: remediationDecisionSchema.nullable(),
  latestApprovalResolution: decisionApprovalResolutionSchema.nullable(),
  recommendations: z.array(analystRecommendationSchema),
  topVulnerabilities: z.array(decisionVulnSchema),
  openVulnerabilities: z.array(decisionVulnSchema),
  riskScore: decisionRiskSchema.nullable(),
  sla: decisionSlaSchema.nullable(),
  patchAssessment: patchAssessmentSchema,
  threatIntel: threatIntelSchema,
})

export const decisionListItemSchema = z.object({
  remediationCaseId: z.string().uuid(),
  softwareName: z.string(),
  softwareOwnerTeamName: z.string().nullable(),
  softwareOwnerAssignmentSource: z.string(),
  criticality: z.string(),
  outcome: z.string().nullable(),
  approvalStatus: z.string().nullable(),
  decidedAt: z.string().nullable(),
  maintenanceWindowDate: z.string().nullable(),
  expiryDate: z.string().nullable(),
  totalVulnerabilities: z.number(),
  criticalCount: z.number(),
  highCount: z.number(),
  riskScore: z.number().nullable(),
  riskBand: z.string().nullable(),
  slaStatus: z.string().nullable(),
  slaDueDate: z.string().nullable(),
  affectedDeviceCount: z.number(),
  openEpisodeTrend: z.array(z.object({
    day: z.string(),
    openEpisodeCount: z.number(),
  })),
  workflowStage: z.string().nullable().optional(),
})

export const decisionListSummarySchema = z.object({
  softwareInScope: z.number(),
  withDecision: z.number(),
  pendingApproval: z.number(),
  noDecision: z.number(),
})

export const pagedDecisionListSchema = z.object({
  items: z.array(decisionListItemSchema),
  totalCount: z.number(),
  page: z.number(),
  pageSize: z.number(),
  totalPages: z.number(),
  summary: decisionListSummarySchema,
})

export type DecisionContext = z.infer<typeof decisionContextSchema>
export type RemediationDecision = z.infer<typeof remediationDecisionSchema>
export type AnalystRecommendation = z.infer<typeof analystRecommendationSchema>
export type DecisionBusinessLabel = z.infer<typeof decisionBusinessLabelSchema>
export type DecisionVuln = z.infer<typeof decisionVulnSchema>
export type DecisionSummary = z.infer<typeof decisionSummarySchema>
export type DecisionWorkflowSummary = z.infer<typeof decisionWorkflowSummarySchema>
export type DecisionWorkflowState = z.infer<typeof decisionWorkflowStateSchema>
export type DecisionWorkflowStage = z.infer<typeof decisionWorkflowStageSchema>
export type DecisionRisk = z.infer<typeof decisionRiskSchema>
export type DecisionSla = z.infer<typeof decisionSlaSchema>
export type PatchAssessment = z.infer<typeof patchAssessmentSchema>
export type DecisionApprovalResolution = z.infer<typeof decisionApprovalResolutionSchema>
export type VulnerabilityOverride = z.infer<typeof vulnerabilityOverrideSchema>
export type ThreatIntel = z.infer<typeof threatIntelSchema>
export type DecisionListItem = z.infer<typeof decisionListItemSchema>
export type DecisionListSummary = z.infer<typeof decisionListSummarySchema>
export type PagedDecisionList = z.infer<typeof pagedDecisionListSchema>
