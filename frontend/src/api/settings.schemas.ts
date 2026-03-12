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
  activeIngestionRunId: z.string().uuid().nullable(),
  leaseExpiresAt: nullableIsoDateTimeSchema,
  activeSnapshotStatus: z.string().nullable(),
  buildingSnapshotStatus: z.string().nullable(),
  activePhase: z.string().nullable(),
  activeBatchNumber: z.number().nullable(),
  activeCheckpointStatus: z.string().nullable(),
  activeRecordsCommitted: z.number().nullable(),
  activeCheckpointCommittedAt: nullableIsoDateTimeSchema,
})

export const tenantIngestionRunSchema = z.object({
  id: z.string().uuid(),
  startedAt: isoDateTimeSchema,
  completedAt: nullableIsoDateTimeSchema,
  status: z.string(),
  stagedMachineCount: z.number(),
  stagedVulnerabilityCount: z.number(),
  stagedSoftwareCount: z.number(),
  persistedMachineCount: z.number(),
  persistedVulnerabilityCount: z.number(),
  persistedSoftwareCount: z.number(),
  error: z.string(),
  snapshotStatus: z.string().nullable(),
  latestPhase: z.string().nullable(),
  latestBatchNumber: z.number().nullable(),
  latestCheckpointStatus: z.string().nullable(),
  latestRecordsCommitted: z.number().nullable(),
  lastCheckpointCommittedAt: nullableIsoDateTimeSchema,
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
