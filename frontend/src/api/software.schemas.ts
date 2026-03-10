import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const normalizedSoftwareVersionCohortSchema = z.object({
  version: z.string().nullable(),
  activeInstallCount: z.number(),
  deviceCount: z.number(),
  activeVulnerabilityCount: z.number(),
  firstSeenAt: isoDateTimeSchema,
  lastSeenAt: isoDateTimeSchema,
})

export const normalizedSoftwareSourceAliasSchema = z.object({
  sourceSystem: z.string(),
  externalSoftwareId: z.string(),
  rawName: z.string(),
  rawVendor: z.string().nullable(),
  rawVersion: z.string().nullable(),
  aliasConfidence: z.string(),
  matchReason: z.string(),
})

export const normalizedSoftwareDetailSchema = z.object({
  id: z.string().uuid(),
  canonicalName: z.string(),
  canonicalVendor: z.string().nullable(),
  primaryCpe23Uri: z.string().nullable(),
  normalizationMethod: z.string(),
  confidence: z.string(),
  firstSeenAt: isoDateTimeSchema.nullable(),
  lastSeenAt: isoDateTimeSchema.nullable(),
  activeInstallCount: z.number(),
  uniqueDeviceCount: z.number(),
  vulnerableInstallCount: z.number(),
  activeVulnerabilityCount: z.number(),
  versionCount: z.number(),
  versionCohorts: z.array(normalizedSoftwareVersionCohortSchema),
  sourceAliases: z.array(normalizedSoftwareSourceAliasSchema),
})

export const normalizedSoftwareListItemSchema = z.object({
  id: z.string().uuid(),
  canonicalName: z.string(),
  canonicalVendor: z.string().nullable(),
  confidence: z.string(),
  normalizationMethod: z.string(),
  primaryCpe23Uri: z.string().nullable(),
  activeInstallCount: z.number(),
  uniqueDeviceCount: z.number(),
  activeVulnerabilityCount: z.number(),
  versionCount: z.number(),
  lastSeenAt: isoDateTimeSchema.nullable(),
})

export const normalizedSoftwareInstallationSchema = z.object({
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
  ownerTeamId: z.string().uuid().nullable(),
  openVulnerabilityCount: z.number(),
})

export const pagedNormalizedSoftwareInstallationsSchema = pagedResponseMetaSchema.extend({
  items: z.array(normalizedSoftwareInstallationSchema),
})

export const pagedNormalizedSoftwareSchema = pagedResponseMetaSchema.extend({
  items: z.array(normalizedSoftwareListItemSchema),
})

export const normalizedSoftwareVulnerabilityEvidenceSchema = z.object({
  method: z.string(),
  confidence: z.string(),
  evidence: z.string(),
  firstSeenAt: isoDateTimeSchema,
  lastSeenAt: isoDateTimeSchema,
  resolvedAt: isoDateTimeSchema.nullable(),
})

export const normalizedSoftwareVulnerabilitySchema = z.object({
  vulnerabilityId: z.string().uuid(),
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
  evidence: z.array(normalizedSoftwareVulnerabilityEvidenceSchema),
})

export type NormalizedSoftwareDetail = z.infer<typeof normalizedSoftwareDetailSchema>
export type NormalizedSoftwareListItem = z.infer<typeof normalizedSoftwareListItemSchema>
export type NormalizedSoftwareVersionCohort = z.infer<typeof normalizedSoftwareVersionCohortSchema>
export type NormalizedSoftwareInstallation = z.infer<typeof normalizedSoftwareInstallationSchema>
export type PagedNormalizedSoftwareInstallations = z.infer<typeof pagedNormalizedSoftwareInstallationsSchema>
export type NormalizedSoftwareVulnerability = z.infer<typeof normalizedSoftwareVulnerabilitySchema>
