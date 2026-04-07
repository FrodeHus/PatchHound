import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { buildFilterParams } from './utils'
import {
  createScanRunnerResponseSchema,
  pagedConnectionProfilesSchema,
  pagedScanProfilesSchema,
  pagedScanRunnersSchema,
  pagedScanRunsSchema,
  pagedScanningToolsSchema,
  rotateSecretResponseSchema,
  scanProfileSchema,
  scanRunDetailSchema,
  scanningToolSchema,
  scanningToolVersionListSchema,
  triggerRunResponseSchema,
} from './authenticated-scans.schemas'

// ─── Scan Profiles ───

export const fetchScanProfiles = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanProfilesSchema.parse(await apiGet(`/scan-profiles?${params}`, context))
  })

export const createScanProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string().min(1),
      description: z.string(),
      cronSchedule: z.string(),
      connectionProfileId: z.string().uuid(),
      scanRunnerId: z.string().uuid(),
      enabled: z.boolean(),
      toolIds: z.array(z.string().uuid()),
    }),
  )
  .handler(async ({ context, data }) => {
    return scanProfileSchema.parse(await apiPost('/scan-profiles', context, data))
  })

export const updateScanProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      cronSchedule: z.string(),
      connectionProfileId: z.string().uuid(),
      scanRunnerId: z.string().uuid(),
      enabled: z.boolean(),
      toolIds: z.array(z.string().uuid()),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    return scanProfileSchema.parse(await apiPut(`/scan-profiles/${id}`, context, body))
  })

export const deleteScanProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/scan-profiles/${id}`, context)
  })

export const triggerScanRun = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return triggerRunResponseSchema.parse(
      await apiPost(`/scan-profiles/${id}/trigger`, context, { triggerKind: 'manual' }),
    )
  })

// ─── Scanning Tools ───

export const fetchScanningTools = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanningToolsSchema.parse(await apiGet(`/scanning-tools?${params}`, context))
  })

export const createScanningTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string().min(1),
      description: z.string(),
      scriptType: z.enum(['python', 'bash', 'powershell']),
      interpreterPath: z.string().min(1),
      timeoutSeconds: z.number().min(5).max(3600),
      initialScript: z.string(),
    }),
  )
  .handler(async ({ context, data }) => {
    await apiPost('/scanning-tools', context, data)
  })

export const updateScanningTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      scriptType: z.enum(['python', 'bash', 'powershell']),
      interpreterPath: z.string().min(1),
      timeoutSeconds: z.number().min(5).max(3600),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    await apiPut(`/scanning-tools/${id}`, context, body)
  })

export const deleteScanningTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/scanning-tools/${id}`, context)
  })

export const fetchScanningTool = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return scanningToolSchema.parse(await apiGet(`/scanning-tools/${id}`, context))
  })

export const fetchToolVersions = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ toolId: z.string().uuid() }))
  .handler(async ({ context, data: { toolId } }) => {
    return scanningToolVersionListSchema.parse(
      await apiGet(`/scanning-tools/${toolId}/versions`, context),
    )
  })

export const publishToolVersion = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ toolId: z.string().uuid(), scriptContent: z.string().min(1) }))
  .handler(async ({ context, data: { toolId, scriptContent } }) => {
    await apiPost(`/scanning-tools/${toolId}/versions`, context, { scriptContent })
  })

// ─── Connection Profiles ───

export const fetchConnectionProfiles = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedConnectionProfilesSchema.parse(await apiGet(`/connection-profiles?${params}`, context))
  })

export const createConnectionProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string().min(1),
      description: z.string(),
      sshHost: z.string().min(1),
      sshPort: z.number().min(1).max(65535),
      sshUsername: z.string().min(1),
      authMethod: z.enum(['password', 'privateKey']),
      password: z.string().optional(),
      privateKey: z.string().optional(),
      passphrase: z.string().optional(),
      hostKeyFingerprint: z.string().optional(),
    }),
  )
  .handler(async ({ context, data }) => {
    await apiPost('/connection-profiles', context, data)
  })

export const updateConnectionProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      sshHost: z.string().min(1),
      sshPort: z.number().min(1).max(65535),
      sshUsername: z.string().min(1),
      authMethod: z.enum(['password', 'privateKey']),
      password: z.string().optional(),
      privateKey: z.string().optional(),
      passphrase: z.string().optional(),
      hostKeyFingerprint: z.string().optional(),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    await apiPut(`/connection-profiles/${id}`, context, body)
  })

export const deleteConnectionProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/connection-profiles/${id}`, context)
  })

// ─── Scan Runners ───

export const fetchScanRunners = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanRunnersSchema.parse(await apiGet(`/scan-runners?${params}`, context))
  })

export const createScanRunner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ name: z.string().min(1), description: z.string() }))
  .handler(async ({ context, data }) => {
    return createScanRunnerResponseSchema.parse(await apiPost('/scan-runners', context, data))
  })

export const updateScanRunner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      enabled: z.boolean(),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    await apiPut(`/scan-runners/${id}`, context, body)
  })

export const rotateScanRunnerSecret = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return rotateSecretResponseSchema.parse(await apiPost(`/scan-runners/${id}/rotate-secret`, context, {}))
  })

export const deleteScanRunner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/scan-runners/${id}`, context)
  })

// ─── Scan Runs ───

export const fetchScanRuns = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      profileId: z.string().uuid().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanRunsSchema.parse(await apiGet(`/authenticated-scan-runs?${params}`, context))
  })

export const fetchScanRunDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return scanRunDetailSchema.parse(await apiGet(`/authenticated-scan-runs/${id}`, context))
  })
