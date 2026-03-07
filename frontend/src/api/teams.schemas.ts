import { z } from 'zod'

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

export const pagedTeamsSchema = z.object({
  items: z.array(teamSchema),
  totalCount: z.number(),
})

export type TeamItem = z.infer<typeof teamSchema>
export type TeamDetail = z.infer<typeof teamDetailSchema>
