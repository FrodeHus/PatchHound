import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const securityProfileSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  environmentClass: z.string(),
  internetReachability: z.string(),
  confidentialityRequirement: z.string(),
  integrityRequirement: z.string(),
  availabilityRequirement: z.string(),
  updatedAt: isoDateTimeSchema,
})

export const pagedSecurityProfilesSchema = pagedResponseMetaSchema.extend({
  items: z.array(securityProfileSchema),
})

export type SecurityProfile = z.infer<typeof securityProfileSchema>
export type PagedSecurityProfiles = z.infer<typeof pagedSecurityProfilesSchema>
