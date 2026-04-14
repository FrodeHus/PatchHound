import { z } from 'zod'

export const featureFlagSchema = z.object({
  displayName: z.string(),
  stage: z.string(),
  isEnabled: z.boolean(),
})

export const featuresResponseSchema = z.record(z.string(), featureFlagSchema)

export const featureFlagOverrideSchema = z.object({
  id: z.string().uuid(),
  flagName: z.string(),
  tenantId: z.string().uuid().nullable(),
  userId: z.string().uuid().nullable(),
  isEnabled: z.boolean(),
  createdAt: z.string().datetime({ offset: true }),
  expiresAt: z.string().datetime({ offset: true }).nullable(),
})

export const adminFeatureFlagSchema = z.object({
  flagName: z.string(),
  displayName: z.string(),
  stage: z.string(),
  isEnabled: z.boolean(),
})

export const upsertFeatureFlagOverrideSchema = z.object({
  flagName: z.string(),
  tenantId: z.string().uuid().nullable().optional(),
  userId: z.string().uuid().nullable().optional(),
  isEnabled: z.boolean(),
  expiresAt: z.string().datetime({ offset: true }).nullable().optional(),
})

export type FeatureFlag = z.infer<typeof featureFlagSchema>
export type FeaturesResponse = z.infer<typeof featuresResponseSchema>
export type FeatureFlagOverride = z.infer<typeof featureFlagOverrideSchema>
export type AdminFeatureFlag = z.infer<typeof adminFeatureFlagSchema>
export type UpsertFeatureFlagOverrideInput = z.infer<typeof upsertFeatureFlagOverrideSchema>
