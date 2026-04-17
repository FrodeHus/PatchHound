import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'
import { nullableIsoDateTimeSchema } from './common.schemas'

export const cloudApplicationListItemSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  credentialCount: z.number(),
  expiredCredentialCount: z.number(),
  expiringCredentialCount: z.number(),
  nextExpiryAt: nullableIsoDateTimeSchema,
})

export const pagedCloudApplicationsSchema = pagedResponseMetaSchema.extend({
  items: z.array(cloudApplicationListItemSchema),
})

export type CloudApplicationListItem = z.infer<typeof cloudApplicationListItemSchema>
