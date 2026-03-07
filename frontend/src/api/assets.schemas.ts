import { z } from 'zod'

export const assetSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  name: z.string(),
  assetType: z.string(),
  criticality: z.string(),
  ownerType: z.string(),
  vulnerabilityCount: z.number(),
})

export const assetVulnerabilitySchema = z.object({
  vulnerabilityId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  status: z.string(),
  detectedDate: z.string(),
  resolvedDate: z.string().nullable(),
})

export const assetDetailSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  name: z.string(),
  description: z.string().nullable(),
  assetType: z.string(),
  criticality: z.string(),
  ownerType: z.string(),
  ownerUserId: z.string().uuid().nullable(),
  ownerTeamId: z.string().uuid().nullable(),
  fallbackTeamId: z.string().uuid().nullable(),
  deviceHealthStatus: z.string().nullable(),
  deviceOsPlatform: z.string().nullable(),
  deviceOsVersion: z.string().nullable(),
  deviceRiskScore: z.string().nullable(),
  deviceLastSeenAt: z.string().nullable(),
  deviceLastIpAddress: z.string().nullable(),
  deviceAadDeviceId: z.string().nullable(),
  metadata: z.string(),
  vulnerabilities: z.array(assetVulnerabilitySchema),
})

export const pagedAssetsSchema = z.object({
  items: z.array(assetSchema),
  totalCount: z.number(),
})

export type Asset = z.infer<typeof assetSchema>
export type AssetDetail = z.infer<typeof assetDetailSchema>
export type PagedAssets = z.infer<typeof pagedAssetsSchema>
