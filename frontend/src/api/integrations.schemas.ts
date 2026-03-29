import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'

export const sentinelConnectorSchema = z.object({
  enabled: z.boolean(),
  dceEndpoint: z.string(),
  dcrImmutableId: z.string(),
  streamName: z.string(),
  tenantId: z.string(),
  clientId: z.string(),
  hasSecret: z.boolean(),
  updatedAt: isoDateTimeSchema.nullable(),
})

export type SentinelConnectorConfig = z.infer<typeof sentinelConnectorSchema>

export const updateSentinelConnectorSchema = z.object({
  enabled: z.boolean(),
  dceEndpoint: z.string().max(512),
  dcrImmutableId: z.string().max(256),
  streamName: z.string().max(256),
  tenantId: z.string().max(128),
  clientId: z.string().max(128),
  clientSecret: z.string().nullable().optional(),
})

export type UpdateSentinelConnectorInput = z.infer<typeof updateSentinelConnectorSchema>
