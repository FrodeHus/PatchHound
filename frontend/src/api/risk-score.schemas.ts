import { z } from 'zod'

const rollupRiskExplanationSchema = z.object({
  score: z.number(),
  calculationVersion: z.string(),
  maxAssetRiskScore: z.number(),
  topThreeAverage: z.number(),
  maxAssetContribution: z.number(),
  topThreeContribution: z.number(),
  assetCount: z.number(),
  openEpisodeCount: z.number(),
  criticalEpisodeCount: z.number(),
  highEpisodeCount: z.number(),
  mediumEpisodeCount: z.number(),
  lowEpisodeCount: z.number(),
  criticalContribution: z.number(),
  highContribution: z.number(),
  mediumContribution: z.number(),
  lowContribution: z.number(),
  factors: z.array(z.object({
    name: z.string(),
    description: z.string(),
    impact: z.number(),
  })),
})

export const riskAssetScoreSummarySchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  overallScore: z.number(),
  maxEpisodeRiskScore: z.number(),
  criticalCount: z.number(),
  highCount: z.number(),
  mediumCount: z.number(),
  lowCount: z.number(),
  openEpisodeCount: z.number(),
  episodeDrivers: z.array(z.object({
    tenantVulnerabilityId: z.string().uuid(),
    externalId: z.string(),
    title: z.string(),
    riskBand: z.string(),
    episodeRiskScore: z.number(),
    threatScore: z.number(),
    contextScore: z.number(),
    operationalScore: z.number(),
  })),
})

export const riskScoreSnapshotSchema = z.object({
  date: z.string(),
  overallScore: z.number(),
  assetCount: z.number(),
  criticalAssetCount: z.number(),
  highAssetCount: z.number(),
})

export const riskScoreSummarySchema = z.object({
  overallScore: z.number(),
  assetCount: z.number(),
  criticalAssetCount: z.number(),
  highAssetCount: z.number(),
  topRiskAssets: z.array(riskAssetScoreSummarySchema),
  history: z.array(riskScoreSnapshotSchema),
  calculatedAt: z.string().datetime({ offset: true }).nullable(),
})

export const deviceGroupRiskDetailSchema = z.object({
  deviceGroupName: z.string(),
  overallScore: z.number(),
  calculatedAt: z.string().datetime({ offset: true }),
  assetCount: z.number(),
  openEpisodeCount: z.number(),
  criticalEpisodeCount: z.number(),
  highEpisodeCount: z.number(),
  mediumEpisodeCount: z.number(),
  lowEpisodeCount: z.number(),
  explanation: rollupRiskExplanationSchema.nullable(),
  topRiskAssets: z.array(riskAssetScoreSummarySchema),
})

export const softwareRiskDetailSchema = z.object({
  tenantSoftwareId: z.string().uuid(),
  softwareName: z.string(),
  vendor: z.string().nullable(),
  overallScore: z.number(),
  calculatedAt: z.string().datetime({ offset: true }),
  affectedDeviceCount: z.number(),
  openEpisodeCount: z.number(),
  criticalEpisodeCount: z.number(),
  highEpisodeCount: z.number(),
  mediumEpisodeCount: z.number(),
  lowEpisodeCount: z.number(),
  topRiskAssets: z.array(riskAssetScoreSummarySchema),
})

export type RiskScoreSummary = z.infer<typeof riskScoreSummarySchema>
export type RiskAssetScoreSummary = z.infer<typeof riskAssetScoreSummarySchema>
export type RiskScoreSnapshot = z.infer<typeof riskScoreSnapshotSchema>
export type RiskAssetEpisodeDriver = z.infer<typeof riskAssetScoreSummarySchema>['episodeDrivers'][number]
export type DeviceGroupRiskDetail = z.infer<typeof deviceGroupRiskDetailSchema>
export type SoftwareRiskDetail = z.infer<typeof softwareRiskDetailSchema>
