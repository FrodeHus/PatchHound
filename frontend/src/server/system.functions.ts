import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
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
})

export const unsealOpenBao = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      keys: z.array(z.string().min(1)).length(3),
    }),
  )
  .handler(async ({ context, data }) => {
    const response = await apiPost('/system/openbao/unseal', context.token, data)
    return systemStatusSchema.parse(response)
  })

export const fetchEnrichmentSources = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const response = await apiGet('/system/enrichment-sources', context.token)
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
    await apiPut('/system/enrichment-sources', context.token, data)
  })

export type EnrichmentSource = z.infer<typeof enrichmentSourceSchema>
