import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

// Define types explicitly to avoid z.lazy inference issues
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

export type AssetRuleOperation = {
  type: string
  parameters: Record<string, string>
}

// Schemas — use passthrough/any for the recursive filter tree since
// runtime validation of deeply nested JSON is less important than type safety
export const filterNodeSchema: z.ZodType<FilterNode> = z.any()

export const assetRuleOperationSchema = z.object({
  type: z.string(),
  parameters: z.record(z.string(), z.string()),
})

export const assetRuleSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  priority: z.number(),
  enabled: z.boolean(),
  filterDefinition: filterNodeSchema,
  operations: z.array(assetRuleOperationSchema),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
  lastExecutedAt: isoDateTimeSchema.nullable(),
  lastMatchCount: z.number().nullable(),
})

export const pagedAssetRulesSchema = pagedResponseMetaSchema.extend({
  items: z.array(assetRuleSchema),
})

export const filterPreviewSchema = z.object({
  count: z.number(),
  samples: z.array(
    z.object({
      id: z.string().uuid(),
      name: z.string(),
      assetType: z.string(),
    }),
  ),
})

export type AssetRule = z.infer<typeof assetRuleSchema>
export type FilterPreview = z.infer<typeof filterPreviewSchema>
