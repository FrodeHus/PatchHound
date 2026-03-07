import { z } from 'zod'

export const tenantSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  entraTenantId: z.string(),
  settings: z.string(),
})

export const pagedTenantSchema = z.object({
  items: z.array(tenantSchema),
  totalCount: z.number(),
})

export type TenantItem = z.infer<typeof tenantSchema>
