import { z } from 'zod'

export const assetSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  name: z.string(),
  assetType: z.string(),
  criticality: z.string(),
  ownerType: z.string(),
  vulnerabilityCount: z.number(),
})

export const pagedAssetsSchema = z.object({
  items: z.array(assetSchema),
  totalCount: z.number(),
})

export type Asset = z.infer<typeof assetSchema>
