import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { pagedResponseMetaSchema } from '@/api/pagination.schemas'
import { z } from 'zod'

const systemStatusSchema = z.object({
  openBaoAvailable: z.boolean(),
  openBaoInitialized: z.boolean(),
  openBaoSealed: z.boolean(),
})

const enrichmentSourceSchema = z.object({
  key: z.string(),
  displayName: z.string(),
  enabled: z.boolean(),
  credentials: z.object({
    hasSecret: z.boolean(),
    apiBaseUrl: z.string(),
  }),
  runtime: z.object({
    lastStartedAt: z.string().nullable(),
    lastCompletedAt: z.string().nullable(),
    lastSucceededAt: z.string().nullable(),
    lastStatus: z.string(),
    lastError: z.string(),
  }),
  queue: z.object({
    pendingCount: z.number(),
    retryScheduledCount: z.number(),
    runningCount: z.number(),
    failedCount: z.number(),
    oldestPendingAt: z.string().nullable(),
  }),
  recentRuns: z.array(z.object({
    id: z.string().uuid(),
    startedAt: z.string(),
    completedAt: z.string().nullable(),
    status: z.string(),
    jobsClaimed: z.number(),
    jobsSucceeded: z.number(),
    jobsNoData: z.number(),
    jobsFailed: z.number(),
    jobsRetried: z.number(),
    lastError: z.string(),
  })),
})

const pagedEnrichmentRunSchema = pagedResponseMetaSchema.extend({
  items: z.array(z.object({
    id: z.string().uuid(),
    startedAt: z.string(),
    completedAt: z.string().nullable(),
    status: z.string(),
    jobsClaimed: z.number(),
    jobsSucceeded: z.number(),
    jobsNoData: z.number(),
    jobsFailed: z.number(),
    jobsRetried: z.number(),
    lastError: z.string(),
  })),
})

export const unsealOpenBao = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      keys: z.array(z.string().min(1)).length(3),
    }),
  )
  .handler(async ({ context, data }) => {
    const response = await apiPost('/system/openbao/unseal', context, data)
    return systemStatusSchema.parse(response)
  })

export const fetchEnrichmentSources = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const response = await apiGet('/system/enrichment-sources', context)
    return z.array(enrichmentSourceSchema).parse(response)
  })

export const updateEnrichmentSources = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.array(z.object({
    key: z.string(),
    displayName: z.string().min(1),
    enabled: z.boolean(),
    credentials: z.object({
      secret: z.string(),
      apiBaseUrl: z.string().min(1),
    }),
  })))
  .handler(async ({ context, data }) => {
    await apiPut('/system/enrichment-sources', context, data)
  })

export const fetchEnrichmentRuns = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    sourceKey: z.string(),
    page: z.number().optional(),
    pageSize: z.number().optional(),
  }))
  .handler(async ({ context, data: { sourceKey, page, pageSize } }) => {
    const queryPage = page ?? 1
    const queryPageSize = pageSize ?? 10
    const response = await apiGet(
      `/system/enrichment-sources/${sourceKey}/runs?page=${queryPage}&pageSize=${queryPageSize}`,
      context,
    )
    return pagedEnrichmentRunSchema.parse(response)
  })

export type EnrichmentSource = z.infer<typeof enrichmentSourceSchema>
export type EnrichmentRun = z.infer<typeof pagedEnrichmentRunSchema>['items'][number]
