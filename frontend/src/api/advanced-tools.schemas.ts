import { z } from 'zod'

export const advancedToolSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  supportedAssetTypes: z.array(z.string()),
  kqlQuery: z.string(),
  aiPrompt: z.string(),
  enabled: z.boolean(),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const advancedToolParameterSchema = z.object({
  name: z.string(),
  description: z.string(),
})

export const advancedToolCatalogSchema = z.object({
  tools: z.array(advancedToolSchema),
  availableParameters: z.array(advancedToolParameterSchema),
})

export const advancedToolSchemaColumnSchema = z.object({
  name: z.string(),
  type: z.string(),
})

export const advancedToolExecutionResultSchema = z.object({
  schema: z.array(advancedToolSchemaColumnSchema),
  results: z.array(z.record(z.string(), z.any())),
  renderedQuery: z.string(),
})

export const advancedToolAiSummaryResultSchema = z.object({
  renderedQuery: z.string(),
  content: z.string(),
  profileName: z.string(),
  providerType: z.string(),
  model: z.string(),
  generatedAt: z.string(),
})

export const advancedToolRenderedQuerySchema = z.object({
  label: z.string(),
  vulnerabilityId: z.string().uuid().nullable(),
  vulnerabilityExternalId: z.string().nullable(),
  query: z.string(),
  schema: z.array(advancedToolSchemaColumnSchema),
  results: z.array(z.record(z.string(), z.any())),
})

export const advancedToolAssetAiReportSchema = z.object({
  content: z.string(),
  profileName: z.string(),
  providerType: z.string(),
  model: z.string(),
  generatedAt: z.string(),
})

export const advancedToolMergedResultSchema = z.object({
  schema: z.array(advancedToolSchemaColumnSchema),
  rows: z.array(z.record(z.string(), z.any())),
  rowCount: z.number(),
})

export const advancedToolAssetExecutionResultSchema = z.object({
  rawResults: advancedToolMergedResultSchema,
  report: advancedToolAssetAiReportSchema.nullable(),
  aiUnavailableMessage: z.string().nullable(),
})

export type AdvancedTool = z.infer<typeof advancedToolSchema>
export type AdvancedToolCatalog = z.infer<typeof advancedToolCatalogSchema>
export type AdvancedToolExecutionResult = z.infer<typeof advancedToolExecutionResultSchema>
export type AdvancedToolAssetExecutionResult = z.infer<typeof advancedToolAssetExecutionResultSchema>
export type AdvancedToolAiSummaryResult = z.infer<typeof advancedToolAiSummaryResultSchema>
