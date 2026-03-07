import { z } from 'zod'

export const teamSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  memberCount: z.number(),
})

export const pagedTeamsSchema = z.object({
  items: z.array(teamSchema),
  totalCount: z.number(),
})

export type TeamItem = z.infer<typeof teamSchema>
