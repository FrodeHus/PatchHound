import { z } from 'zod'

export const campaignSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  status: z.string(),
  createdAt: z.string().datetime(),
  vulnerabilityCount: z.number(),
  totalTasks: z.number(),
  completedTasks: z.number(),
})

export const campaignDetailSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  status: z.string(),
  createdBy: z.string().uuid(),
  createdAt: z.string().datetime(),
  vulnerabilityCount: z.number(),
  totalTasks: z.number(),
  completedTasks: z.number(),
  vulnerabilityIds: z.array(z.string().uuid()),
})

export const pagedCampaignSchema = z.object({
  items: z.array(campaignSchema),
  totalCount: z.number(),
})

export type Campaign = z.infer<typeof campaignSchema>
export type CampaignDetail = z.infer<typeof campaignDetailSchema>
