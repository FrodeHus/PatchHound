import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'
import { isoDateTimeSchema, nullableIsoDateTimeSchema } from './common.schemas'

export const cloudApplicationListItemSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  credentialCount: z.number(),
  expiredCredentialCount: z.number(),
  expiringCredentialCount: z.number(),
  nextExpiryAt: nullableIsoDateTimeSchema,
})

export const cloudApplicationCredentialSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  type: z.string(),
  displayName: z.string().nullable(),
  expiresAt: isoDateTimeSchema,
})

export const cloudApplicationDetailSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  appId: z.string().nullable(),
  name: z.string(),
  description: z.string().nullable(),
  isFallbackPublicClient: z.boolean(),
  redirectUris: z.array(z.string()),
  ownerTeamId: z.string().uuid().nullable(),
  ownerTeamName: z.string().nullable(),
  credentials: z.array(cloudApplicationCredentialSchema),
})

export const pagedCloudApplicationsSchema = pagedResponseMetaSchema.extend({
  items: z.array(cloudApplicationListItemSchema),
})

export type CloudApplicationListItem = z.infer<typeof cloudApplicationListItemSchema>
export type CloudApplicationDetail = z.infer<typeof cloudApplicationDetailSchema>
export type CloudApplicationCredential = z.infer<typeof cloudApplicationCredentialSchema>
