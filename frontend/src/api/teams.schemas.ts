import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'
import { filterNodeSchema } from './device-rules.schemas'
import { isoDateTimeSchema } from './common.schemas'

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

export const teamSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  name: z.string(),
  isDefault: z.boolean(),
  isDynamic: z.boolean(),
  memberCount: z.number(),
  currentRiskScore: z.number().nullable(),
})

export const teamDetailSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  name: z.string(),
  isDefault: z.boolean(),
  isDynamic: z.boolean(),
  assignedAssetCount: z.number(),
  currentRiskScore: z.number().nullable(),
  riskExplanation: rollupRiskExplanationSchema.nullable(),
  topRiskAssets: z.array(z.object({
    assetId: z.string().uuid(),
    assetName: z.string(),
    assetType: z.string(),
    currentRiskScore: z.number(),
    maxEpisodeRiskScore: z.number(),
    openEpisodeCount: z.number(),
  })),
  members: z.array(z.object({
    userId: z.string().uuid(),
    displayName: z.string(),
    email: z.string(),
  })),
  membershipRule: z.object({
    id: z.string().uuid(),
    filterDefinition: filterNodeSchema,
    createdAt: isoDateTimeSchema,
    updatedAt: isoDateTimeSchema,
    lastExecutedAt: isoDateTimeSchema.nullable(),
    lastMatchCount: z.number().nullable(),
  }).nullable(),
})

export const teamMembershipRulePreviewSchema = z.object({
  count: z.number(),
  samples: z.array(z.object({
    userId: z.string().uuid(),
    displayName: z.string(),
    email: z.string().email(),
    company: z.string().nullable(),
  })),
})

export const pagedTeamsSchema = pagedResponseMetaSchema.extend({
  items: z.array(teamSchema),
})

export type TeamItem = z.infer<typeof teamSchema>
export type TeamDetail = z.infer<typeof teamDetailSchema>
export type TeamMembershipRulePreview = z.infer<typeof teamMembershipRulePreviewSchema>
