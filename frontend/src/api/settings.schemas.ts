import { z } from 'zod'
import { isoDateTimeSchema, nullableIsoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const tenantCredentialsSchema = z.object({
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

export const tenantIngestionRunSchema = z.object({
  id: z.string().uuid(),
  startedAt: isoDateTimeSchema,
  completedAt: nullableIsoDateTimeSchema,
  status: z.string(),
  fetchedVulnerabilityCount: z.number(),
  fetchedAssetCount: z.number(),
  fetchedSoftwareInstallationCount: z.number(),
  stagedVulnerabilityCount: z.number(),
  stagedExposureCount: z.number(),
  mergedExposureCount: z.number(),
  openedProjectionCount: z.number(),
  resolvedProjectionCount: z.number(),
  stagedAssetCount: z.number(),
  mergedAssetCount: z.number(),
  stagedSoftwareLinkCount: z.number(),
  resolvedSoftwareLinkCount: z.number(),
  installationsCreated: z.number(),
  installationsTouched: z.number(),
  installationEpisodesOpened: z.number(),
  installationEpisodesSeen: z.number(),
  staleInstallationsMarked: z.number(),
  installationsRemoved: z.number(),
  error: z.string(),
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
  recentRuns: z.array(tenantIngestionRunSchema),
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

export const pagedTenantSchema = pagedResponseMetaSchema.extend({
  items: z.array(tenantListItemSchema),
})

export const pagedTenantIngestionRunSchema = pagedResponseMetaSchema.extend({
  items: z.array(tenantIngestionRunSchema),
})

export type TenantListItem = z.infer<typeof tenantListItemSchema>
export type TenantDetail = z.infer<typeof tenantDetailSchema>
export type TenantIngestionSource = z.infer<typeof tenantIngestionSourceSchema>
export type TenantIngestionRun = z.infer<typeof tenantIngestionRunSchema>
