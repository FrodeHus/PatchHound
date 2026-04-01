import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'
import { remediationTaskSummarySchema } from './remediation-tasks.schemas'

export const tenantSoftwareVersionCohortSchema = z.object({
  version: z.string().nullable(),
  activeInstallCount: z.number(),
  deviceCount: z.number(),
  activeVulnerabilityCount: z.number(),
  firstSeenAt: isoDateTimeSchema,
  lastSeenAt: isoDateTimeSchema,
})

export const tenantSoftwareSourceAliasSchema = z.object({
  sourceSystem: z.string(),
  externalSoftwareId: z.string(),
  rawName: z.string(),
  rawVendor: z.string().nullable(),
  rawVersion: z.string().nullable(),
  aliasConfidence: z.string(),
  matchReason: z.string(),
})

export const tenantSoftwareDetailSchema = z.object({
  id: z.string().uuid(),
  normalizedSoftwareId: z.string().uuid(),
  primarySoftwareAssetId: z.string().uuid().nullable(),
  canonicalName: z.string(),
  canonicalVendor: z.string().nullable(),
  category: z.string().nullable().optional(),
  primaryCpe23Uri: z.string().nullable(),
  description: z.string().nullable(),
  descriptionGeneratedAt: isoDateTimeSchema.nullable(),
  descriptionProviderType: z.string().nullable(),
  descriptionProfileName: z.string().nullable(),
  descriptionModel: z.string().nullable(),
  normalizationMethod: z.string(),
  confidence: z.string(),
  firstSeenAt: isoDateTimeSchema.nullable(),
  lastSeenAt: isoDateTimeSchema.nullable(),
  activeInstallCount: z.number(),
  uniqueDeviceCount: z.number(),
  vulnerableInstallCount: z.number(),
  activeVulnerabilityCount: z.number(),
  versionCount: z.number(),
  exposureImpactScore: z.number().nullable(),
  exposureImpactExplanation: z.object({
    score: z.number(),
    calculationVersion: z.string(),
    deviceCount: z.number(),
    highValueDeviceCount: z.number(),
    deviceReachWeight: z.number(),
    highValueRatio: z.number(),
    highValueBonus: z.number(),
    vulnerabilityCount: z.number(),
    rawVulnerabilitySum: z.number(),
    vulnerabilityComponent: z.number(),
    rawScore: z.number(),
    vulnerabilityFactors: z.array(z.object({
      externalId: z.string(),
      severity: z.string(),
      cvssScore: z.number().nullable(),
      severityWeight: z.number(),
      normalizedScore: z.number(),
      contribution: z.number(),
    })),
  }).nullable(),
  remediation: remediationTaskSummarySchema,
  versionCohorts: z.array(tenantSoftwareVersionCohortSchema),
  sourceAliases: z.array(tenantSoftwareSourceAliasSchema),
  lifecycle: z.object({
    eolDate: isoDateTimeSchema.nullable(),
    latestVersion: z.string().nullable(),
    isLts: z.boolean().nullable(),
    supportEndDate: isoDateTimeSchema.nullable(),
    isDiscontinued: z.boolean().nullable(),
    enrichedAt: isoDateTimeSchema.nullable(),
    productSlug: z.string().nullable(),
  }).nullable(),
  supplyChainInsight: z.object({
    remediationPath: z.string(),
    confidence: z.string(),
    sourceFormat: z.string().nullable(),
    primaryComponentName: z.string().nullable(),
    primaryComponentVersion: z.string().nullable(),
    fixedVersion: z.string().nullable(),
    affectedVulnerabilityCount: z.number().nullable(),
    summary: z.string(),
    enrichedAt: isoDateTimeSchema.nullable(),
  }).nullable(),
})

export const tenantSoftwareListItemSchema = z.object({
  id: z.string().uuid(),
  normalizedSoftwareId: z.string().uuid(),
  canonicalName: z.string(),
  canonicalVendor: z.string().nullable(),
  currentRiskScore: z.number().nullable(),
  confidence: z.string(),
  normalizationMethod: z.string(),
  primaryCpe23Uri: z.string().nullable(),
  activeInstallCount: z.number(),
  uniqueDeviceCount: z.number(),
  activeVulnerabilityCount: z.number(),
  versionCount: z.number(),
  exposureImpactScore: z.number().nullable(),
  lastSeenAt: isoDateTimeSchema.nullable(),
  maintenanceWindowDate: isoDateTimeSchema.nullable(),
})

export const tenantSoftwareInstallationSchema = z.object({
  tenantSoftwareId: z.string().uuid(),
  deviceAssetId: z.string().uuid(),
  deviceName: z.string(),
  deviceCriticality: z.string(),
  softwareAssetId: z.string().uuid(),
  softwareAssetName: z.string(),
  version: z.string().nullable(),
  firstSeenAt: isoDateTimeSchema,
  lastSeenAt: isoDateTimeSchema,
  removedAt: isoDateTimeSchema.nullable(),
  isActive: z.boolean(),
  currentEpisodeNumber: z.number(),
  securityProfileName: z.string().nullable(),
  ownerUserId: z.string().uuid().nullable(),
  ownerUserName: z.string().nullable(),
  ownerTeamId: z.string().uuid().nullable(),
  ownerTeamName: z.string().nullable(),
  openVulnerabilityCount: z.number(),
})

export const pagedTenantSoftwareInstallationsSchema = pagedResponseMetaSchema.extend({
  items: z.array(tenantSoftwareInstallationSchema),
})

export const pagedTenantSoftwareSchema = pagedResponseMetaSchema.extend({
  items: z.array(tenantSoftwareListItemSchema),
})

export const tenantSoftwareVulnerabilityEvidenceSchema = z.object({
  method: z.string(),
  confidence: z.string(),
  evidence: z.string(),
  firstSeenAt: isoDateTimeSchema,
  lastSeenAt: isoDateTimeSchema,
  resolvedAt: isoDateTimeSchema.nullable(),
})

export const tenantSoftwareVulnerabilitySchema = z.object({
  tenantVulnerabilityId: z.string().uuid(),
  vulnerabilityDefinitionId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  cvssScore: z.number().nullable(),
  publishedDate: isoDateTimeSchema.nullable(),
  source: z.string(),
  bestMatchMethod: z.string(),
  bestConfidence: z.string(),
  affectedInstallCount: z.number(),
  affectedDeviceCount: z.number(),
  affectedVersionCount: z.number(),
  affectedVersions: z.array(z.string()),
  firstSeenAt: isoDateTimeSchema,
  lastSeenAt: isoDateTimeSchema,
  resolvedAt: isoDateTimeSchema.nullable(),
  evidence: z.array(tenantSoftwareVulnerabilityEvidenceSchema),
})

export const tenantSoftwareAiReportSchema = z.object({
  tenantSoftwareId: z.string().uuid(),
  content: z.string(),
  providerType: z.string(),
  profileName: z.string(),
  model: z.string(),
  generatedAt: isoDateTimeSchema,
})

export const tenantSoftwareDescriptionSchema = z.object({
  tenantSoftwareId: z.string().uuid(),
  normalizedSoftwareId: z.string().uuid(),
  description: z.string(),
  providerType: z.string(),
  profileName: z.string(),
  model: z.string(),
  generatedAt: isoDateTimeSchema,
})

export const tenantSoftwareDescriptionJobSchema = z.object({
  id: z.string().uuid(),
  tenantSoftwareId: z.string().uuid(),
  status: z.string(),
  error: z.string().nullable(),
  requestedAt: isoDateTimeSchema,
  startedAt: isoDateTimeSchema.nullable(),
  completedAt: isoDateTimeSchema.nullable(),
})

export type TenantSoftwareDetail = z.infer<typeof tenantSoftwareDetailSchema>
export type TenantSoftwareListItem = z.infer<typeof tenantSoftwareListItemSchema>
export type TenantSoftwareVersionCohort = z.infer<typeof tenantSoftwareVersionCohortSchema>
export type TenantSoftwareInstallation = z.infer<typeof tenantSoftwareInstallationSchema>
export type PagedTenantSoftwareInstallations = z.infer<typeof pagedTenantSoftwareInstallationsSchema>
export type TenantSoftwareVulnerability = z.infer<typeof tenantSoftwareVulnerabilitySchema>
export type TenantSoftwareAiReport = z.infer<typeof tenantSoftwareAiReportSchema>
export type TenantSoftwareDescription = z.infer<typeof tenantSoftwareDescriptionSchema>
export type TenantSoftwareDescriptionJob = z.infer<typeof tenantSoftwareDescriptionJobSchema>
