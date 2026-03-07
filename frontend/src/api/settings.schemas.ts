import { z } from 'zod'
import { nullableIsoDateTimeSchema } from './common.schemas'

export const tenantCredentialsSchema = z.object({
  tenantId: z.string(),
  clientId: z.string(),
  hasSecret: z.boolean(),
  apiBaseUrl: z.string(),
  tokenScope: z.string(),
})

export const tenantIngestionRuntimeSchema = z.object({
  manualRequestedAt: nullableIsoDateTimeSchema,
  lastStartedAt: nullableIsoDateTimeSchema,
  lastCompletedAt: nullableIsoDateTimeSchema,
  lastSucceededAt: nullableIsoDateTimeSchema,
  lastStatus: z.string(),
  lastError: z.string(),
})

export const tenantIngestionSourceSchema = z.object({
  key: z.string(),
  displayName: z.string(),
  enabled: z.boolean(),
  syncSchedule: z.string(),
  supportsScheduling: z.boolean(),
  supportsManualSync: z.boolean(),
  credentials: tenantCredentialsSchema,
  runtime: tenantIngestionRuntimeSchema,
})

export const tenantListItemSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  entraTenantId: z.string(),
  configuredIngestionSourceCount: z.number(),
})

export const tenantDetailSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  entraTenantId: z.string(),
  assets: z.object({
    totalCount: z.number(),
    deviceCount: z.number(),
    softwareCount: z.number(),
    cloudResourceCount: z.number(),
  }),
  sla: z.object({
    criticalDays: z.number(),
    highDays: z.number(),
    mediumDays: z.number(),
    lowDays: z.number(),
  }),
  ingestionSources: z.array(tenantIngestionSourceSchema),
})

export const pagedTenantSchema = z.object({
  items: z.array(tenantListItemSchema),
  totalCount: z.number(),
})

export type TenantListItem = z.infer<typeof tenantListItemSchema>
export type TenantDetail = z.infer<typeof tenantDetailSchema>
export type TenantIngestionSource = z.infer<typeof tenantIngestionSourceSchema>
