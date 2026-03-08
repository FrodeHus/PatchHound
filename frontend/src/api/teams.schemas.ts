import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const teamSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  name: z.string(),
  memberCount: z.number(),
})

export const teamDetailSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  name: z.string(),
  assignedAssetCount: z.number(),
  members: z.array(z.object({
    userId: z.string().uuid(),
    displayName: z.string(),
    email: z.string(),
  })),
})

export const pagedTeamsSchema = pagedResponseMetaSchema.extend({
  items: z.array(teamSchema),
})

export type TeamItem = z.infer<typeof teamSchema>
export type TeamDetail = z.infer<typeof teamDetailSchema>
