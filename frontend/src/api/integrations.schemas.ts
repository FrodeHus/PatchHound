import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'

export const sentinelConnectorSchema = z.object({
  enabled: z.boolean(),
  dceEndpoint: z.string(),
  dcrImmutableId: z.string(),
  streamName: z.string(),
  storedCredentialId: z.string().uuid().nullable(),
  acceptedCredentialTypes: z.array(z.string()),
  updatedAt: isoDateTimeSchema.nullable(),
})

export type SentinelConnectorConfig = z.infer<typeof sentinelConnectorSchema>

export const updateSentinelConnectorSchema = z.object({
  enabled: z.boolean(),
  dceEndpoint: z.string().max(512),
  dcrImmutableId: z.string().max(256),
  streamName: z.string().max(256),
  storedCredentialId: z.string().uuid().nullable(),
})

export type UpdateSentinelConnectorInput = z.infer<typeof updateSentinelConnectorSchema>
