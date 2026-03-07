import { z } from 'zod'

export const tenantCredentialsSchema = z.object({
  tenantId: z.string(),
  clientId: z.string(),
  hasClientSecret: z.boolean(),
  apiBaseUrl: z.string(),
  tokenScope: z.string(),
})

const nullableDateString = z.string().nullable().refine((value) => value === null || !Number.isNaN(Date.parse(value)), {
  message: 'Invalid date',
})

export const tenantIngestionRuntimeSchema = z.object({
  lastStartedAt: nullableDateString,
  lastCompletedAt: nullableDateString,
  lastSucceededAt: nullableDateString,
  lastStatus: z.string(),
  lastError: z.string(),
})

export const tenantIngestionSourceSchema = z.object({
  key: z.string(),
  displayName: z.string(),
  enabled: z.boolean(),
  syncSchedule: z.string(),
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
  ingestionSources: z.array(tenantIngestionSourceSchema),
})

export const pagedTenantSchema = z.object({
  items: z.array(tenantListItemSchema),
  totalCount: z.number(),
})

export type TenantListItem = z.infer<typeof tenantListItemSchema>
export type TenantDetail = z.infer<typeof tenantDetailSchema>
export type TenantIngestionSource = z.infer<typeof tenantIngestionSourceSchema>
