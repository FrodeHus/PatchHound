import { z } from 'zod'

export const tenantCredentialsSchema = z.object({
  tenantId: z.string(),
  clientId: z.string(),
  clientSecret: z.string(),
  apiBaseUrl: z.string(),
  tokenScope: z.string(),
})

export const tenantIngestionSourceSchema = z.object({
  key: z.string(),
  displayName: z.string(),
  enabled: z.boolean(),
  syncSchedule: z.string(),
  credentials: tenantCredentialsSchema,
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
