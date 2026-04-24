import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const assetRuleAssetTypeSchema = z.enum(['Device', 'Software', 'Application'])
export type AssetRuleAssetType = z.infer<typeof assetRuleAssetTypeSchema>

// Phase 1 canonical cleanup (Task 15): schemas for /api/device-rules.
// Replaces the legacy asset-rules surface. Preview items drop the
// `assetType` column since the payload is always a device.

export type FilterCondition = {
  type: 'condition'
  field: string
  operator: string
  value: string
}

export type FilterGroup = {
  type: 'group'
  operator: 'AND' | 'OR'
  conditions: FilterNode[]
}

export type FilterNode = FilterCondition | FilterGroup

export type DeviceRuleOperation = {
  type: string
  parameters: Record<string, string>
}

export const filterNodeSchema: z.ZodType<FilterNode> = z.any()

export const deviceRuleOperationSchema = z.object({
  type: z.string(),
  parameters: z.record(z.string(), z.string()),
})

export const deviceRuleSchema = z.object({
  id: z.string().uuid(),
  assetType: assetRuleAssetTypeSchema,
  name: z.string(),
  description: z.string().nullable(),
  priority: z.number(),
  enabled: z.boolean(),
  filterDefinition: filterNodeSchema,
  operations: z.array(deviceRuleOperationSchema),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
  lastExecutedAt: isoDateTimeSchema.nullable(),
  lastMatchCount: z.number().nullable(),
})

export const pagedDeviceRulesSchema = pagedResponseMetaSchema.extend({
  items: z.array(deviceRuleSchema),
})

export const filterPreviewSchema = z.object({
  count: z.number(),
  samples: z.array(
    z.object({
      id: z.string().uuid(),
      name: z.string(),
    }),
  ),
})

export type DeviceRule = z.infer<typeof deviceRuleSchema>
export type FilterPreview = z.infer<typeof filterPreviewSchema>
