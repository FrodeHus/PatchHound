import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const vulnerabilitySchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  source: z.string(),
  cvssScore: z.number().nullable(),
  publishedDate: isoDateTimeSchema.nullable(),
  exposureDataAvailable: z.boolean(),
  affectedDeviceCount: z.number(),
  threatScore: z.number().nullable(),
  epssScore: z.number().nullable(),
  publicExploit: z.boolean(),
  knownExploited: z.boolean(),
  activeAlert: z.boolean(),
})

export const pagedVulnerabilitySchema = pagedResponseMetaSchema.extend({
  items: z.array(vulnerabilitySchema),
})

export const affectedAssetSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  assetType: z.string(),
  securityProfileName: z.string().nullable(),
  status: z.string(),
  episodeRiskScore: z.number().nullable(),
  episodeRiskBand: z.string().nullable(),
  vendorSeverity: z.string(),
  vendorScore: z.number().nullable(),
  effectiveSeverity: z.string(),
  effectiveScore: z.number().nullable(),
  assessmentReasonSummary: z.string().nullable(),
  detectedDate: isoDateTimeSchema,
  resolvedDate: isoDateTimeSchema.nullable(),
  episodeCount: z.number(),
  episodes: z.array(z.object({
    episodeNumber: z.number(),
    status: z.string(),
    firstSeenAt: isoDateTimeSchema,
    lastSeenAt: isoDateTimeSchema,
    resolvedAt: isoDateTimeSchema.nullable(),
  })),
  possibleCorrelatedSoftware: z.array(z.string()),
})

export const orgSeveritySchema = z.object({
  adjustedSeverity: z.string(),
  justification: z.string(),
  assetCriticalityFactor: z.string().nullable(),
  exposureFactor: z.string().nullable(),
  compensatingControls: z.string().nullable(),
  adjustedAt: isoDateTimeSchema,
})

export const affectedDeviceSchema = z.object({
  deviceId: z.string().uuid(),
  deviceName: z.string(),
  softwareProductId: z.string().uuid().nullable(),
  softwareProductName: z.string().nullable(),
  matchedVersion: z.string().nullable(),
  matchSource: z.string(),
  status: z.string(),
  environmentalCvss: z.number().nullable(),
  environmentalReason: z.string().nullable(),
  firstObservedAt: isoDateTimeSchema,
  lastObservedAt: isoDateTimeSchema,
  resolvedAt: isoDateTimeSchema.nullable(),
})

export const exposureEpisodeSchema = z.object({
  episodeId: z.string().uuid(),
  exposureId: z.string().uuid(),
  episodeNumber: z.number(),
  openedAt: isoDateTimeSchema,
  closedAt: isoDateTimeSchema.nullable(),
})

export const vulnerabilityDetailSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  source: z.string(),
  title: z.string(),
  description: z.string(),
  vendorSeverity: z.string(),
  cvssScore: z.number().nullable(),
  cvssVector: z.string().nullable(),
  publishedDate: isoDateTimeSchema.nullable(),
  references: z.array(z.object({
    url: z.string().url(),
    source: z.string(),
    tags: z.array(z.string()),
  })),
  applicabilities: z.array(z.object({
    softwareProductId: z.string().uuid().nullable(),
    softwareProductName: z.string().nullable(),
    cpeCriteria: z.string().nullable(),
    vulnerable: z.boolean(),
    versionStartIncluding: z.string().nullable(),
    versionStartExcluding: z.string().nullable(),
    versionEndIncluding: z.string().nullable(),
    versionEndExcluding: z.string().nullable(),
  })),
  threatAssessment: z.object({
    threatScore: z.number(),
    epssScore: z.number().nullable(),
    knownExploited: z.boolean(),
    publicExploit: z.boolean(),
    activeAlert: z.boolean(),
  }).nullable(),
  exposures: z.object({
    dataAvailable: z.boolean(),
    dataAvailableReason: z.string().nullable(),
    affectedDevices: z.array(affectedDeviceSchema),
    activeEpisodes: z.array(exposureEpisodeSchema),
    resolvedEpisodes: z.array(exposureEpisodeSchema),
  }),
})

export const aiReportSchema = z.object({
  id: z.string().uuid(),
  tenantVulnerabilityId: z.string().uuid(),
  content: z.string(),
  providerType: z.string(),
  profileName: z.string(),
  model: z.string(),
  generatedAt: isoDateTimeSchema,
})

export const commentSchema = z.object({
  id: z.string().uuid(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  authorId: z.string().uuid(),
  content: z.string(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema.nullable(),
})

export type Vulnerability = z.infer<typeof vulnerabilitySchema>
export type VulnerabilityDetail = z.infer<typeof vulnerabilityDetailSchema>
export type AffectedAsset = z.infer<typeof affectedAssetSchema>
export type AffectedDevice = z.infer<typeof affectedDeviceSchema>
export type ExposureEpisode = z.infer<typeof exposureEpisodeSchema>
export type AiReport = z.infer<typeof aiReportSchema>
export type CommentItem = z.infer<typeof commentSchema>
