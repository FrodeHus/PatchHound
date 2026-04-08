import { z } from 'zod'
import { isoDateTimeSchema, nullableIsoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

// --- Scan Profiles ---

export const scanProfileSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  cronSchedule: z.string(),
  connectionProfileId: z.string().uuid(),
  scanRunnerId: z.string().uuid(),
  enabled: z.boolean(),
  manualRequestedAt: nullableIsoDateTimeSchema,
  lastRunStartedAt: nullableIsoDateTimeSchema,
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
  toolIds: z.array(z.string().uuid()),
})

export const pagedScanProfilesSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanProfileSchema),
})

export type ScanProfile = z.infer<typeof scanProfileSchema>
export type PagedScanProfiles = z.infer<typeof pagedScanProfilesSchema>

// --- Scanning Tools ---

export const scanningToolSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  scriptType: z.string(),
  interpreterPath: z.string(),
  timeoutSeconds: z.number(),
  outputModel: z.string(),
  currentVersionId: z.string().uuid().nullable(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
})

export const pagedScanningToolsSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanningToolSchema),
})

export type ScanningTool = z.infer<typeof scanningToolSchema>
export type PagedScanningTools = z.infer<typeof pagedScanningToolsSchema>

export const scanningToolVersionSchema = z.object({
  id: z.string().uuid(),
  scanningToolId: z.string().uuid(),
  versionNumber: z.number(),
  scriptContent: z.string(),
  editedByUserId: z.string().uuid(),
  editedAt: isoDateTimeSchema,
})

export type ScanningToolVersion = z.infer<typeof scanningToolVersionSchema>

export const scanningToolVersionListSchema = z.array(scanningToolVersionSchema)

// --- Connection Profiles ---

export const connectionProfileSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  sshHost: z.string(),
  sshPort: z.number(),
  sshUsername: z.string(),
  authMethod: z.string(),
  hostKeyFingerprint: z.string().nullable(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
})

export const pagedConnectionProfilesSchema = pagedResponseMetaSchema.extend({
  items: z.array(connectionProfileSchema),
})

export type ConnectionProfile = z.infer<typeof connectionProfileSchema>
export type PagedConnectionProfiles = z.infer<typeof pagedConnectionProfilesSchema>

// --- Scan Runners ---

export const scanRunnerSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  lastSeenAt: nullableIsoDateTimeSchema,
  version: z.string(),
  enabled: z.boolean(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
})

export const pagedScanRunnersSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanRunnerSchema),
})

export const createScanRunnerResponseSchema = z.object({
  runner: scanRunnerSchema,
  bearerSecret: z.string(),
})

export const rotateSecretResponseSchema = z.object({
  bearerSecret: z.string(),
})

export const triggerRunResponseSchema = z.object({
  runId: z.string().uuid(),
})

export type ScanRunner = z.infer<typeof scanRunnerSchema>
export type PagedScanRunners = z.infer<typeof pagedScanRunnersSchema>
export type CreateScanRunnerResponse = z.infer<typeof createScanRunnerResponseSchema>
export type RotateSecretResponse = z.infer<typeof rotateSecretResponseSchema>

// --- Scan Runs ---

export const scanRunSchema = z.object({
  id: z.string().uuid(),
  scanProfileId: z.string().uuid(),
  profileName: z.string(),
  triggerKind: z.string(),
  triggeredByUserId: z.string().uuid().nullable(),
  startedAt: isoDateTimeSchema,
  completedAt: nullableIsoDateTimeSchema,
  status: z.string(),
  totalDevices: z.number(),
  succeededCount: z.number(),
  failedCount: z.number(),
  entriesIngested: z.number(),
})

export const pagedScanRunsSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanRunSchema),
})

export const validationIssueSchema = z.object({
  fieldPath: z.string(),
  message: z.string(),
  entryIndex: z.number(),
})

export const scanJobSummarySchema = z.object({
  id: z.string().uuid(),
  assetId: z.string().uuid(),
  assetName: z.string(),
  status: z.string(),
  attemptCount: z.number(),
  startedAt: nullableIsoDateTimeSchema,
  completedAt: nullableIsoDateTimeSchema,
  errorMessage: z.string(),
  entriesIngested: z.number(),
  validationIssues: z.array(validationIssueSchema),
})

export const scanRunDetailSchema = scanRunSchema.extend({
  jobs: z.array(scanJobSummarySchema),
})

export type ScanRun = z.infer<typeof scanRunSchema>
export type PagedScanRuns = z.infer<typeof pagedScanRunsSchema>
export type ScanJobSummary = z.infer<typeof scanJobSummarySchema>
export type ScanRunDetail = z.infer<typeof scanRunDetailSchema>
export type ValidationIssue = z.infer<typeof validationIssueSchema>

// --- Scan Profile Assignments ---

export const profileAssignedDeviceSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  assignedByRuleId: z.string().uuid().nullable(),
  assignedAt: isoDateTimeSchema,
})

export type ProfileAssignedDevice = z.infer<typeof profileAssignedDeviceSchema>
