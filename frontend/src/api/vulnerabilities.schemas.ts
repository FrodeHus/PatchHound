import { z } from 'zod'

const isoDateTimeSchema = z.string().refine((value) => !Number.isNaN(Date.parse(value)), {
  message: 'Invalid datetime',
})

export const vulnerabilitySchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  status: z.string(),
  source: z.string(),
  cvssScore: z.number().nullable(),
  publishedDate: isoDateTimeSchema.nullable(),
  affectedAssetCount: z.number(),
  adjustedSeverity: z.string().nullable(),
})

export const pagedVulnerabilitySchema = z.object({
  items: z.array(vulnerabilitySchema),
  totalCount: z.number(),
})

export const affectedAssetSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  assetType: z.string(),
  status: z.string(),
  detectedDate: isoDateTimeSchema,
  resolvedDate: isoDateTimeSchema.nullable(),
})

export const orgSeveritySchema = z.object({
  adjustedSeverity: z.string(),
  justification: z.string(),
  assetCriticalityFactor: z.string().nullable(),
  exposureFactor: z.string().nullable(),
  compensatingControls: z.string().nullable(),
  adjustedAt: isoDateTimeSchema,
})

export const vulnerabilityDetailSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  description: z.string(),
  vendorSeverity: z.string(),
  status: z.string(),
  source: z.string(),
  cvssScore: z.number().nullable(),
  cvssVector: z.string().nullable(),
  publishedDate: isoDateTimeSchema.nullable(),
  affectedAssets: z.array(affectedAssetSchema),
  organizationalSeverity: orgSeveritySchema.nullable(),
})

export const aiReportSchema = z.object({
  id: z.string().uuid(),
  vulnerabilityId: z.string().uuid(),
  content: z.string(),
  provider: z.string(),
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
export type AiReport = z.infer<typeof aiReportSchema>
export type CommentItem = z.infer<typeof commentSchema>
