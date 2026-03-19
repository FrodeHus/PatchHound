import { z } from 'zod'

export const scoreFactorSchema = z.object({
  name: z.string(),
  description: z.string(),
  impact: z.number(),
})

export const assetScoreSummarySchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  overallScore: z.number(),
  vulnerabilityScore: z.number(),
  configurationScore: z.number(),
  deviceValueWeight: z.number(),
  activeVulnerabilityCount: z.number(),
})

export const scoreSnapshotSchema = z.object({
  date: z.string(),
  overallScore: z.number(),
  assetCount: z.number(),
})

export const secureScoreSummarySchema = z.object({
  overallScore: z.number(),
  targetScore: z.number(),
  assetCount: z.number(),
  assetsAboveTarget: z.number(),
  topRiskAssets: z.array(assetScoreSummarySchema),
  history: z.array(scoreSnapshotSchema),
})

export const assetScoreDetailSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  overallScore: z.number(),
  vulnerabilityScore: z.number(),
  configurationScore: z.number(),
  deviceValueWeight: z.number(),
  activeVulnerabilityCount: z.number(),
  factors: z.array(scoreFactorSchema),
  calculatedAt: z.string(),
  calculationVersion: z.string(),
})

export type SecureScoreSummary = z.infer<typeof secureScoreSummarySchema>
export type AssetScoreSummary = z.infer<typeof assetScoreSummarySchema>
export type AssetScoreDetail = z.infer<typeof assetScoreDetailSchema>
export type ScoreFactor = z.infer<typeof scoreFactorSchema>
export type ScoreSnapshot = z.infer<typeof scoreSnapshotSchema>
