import { z } from 'zod'

export const setupStatusSchema = z.object({
  isInitialized: z.boolean(),
})

export const setupPayloadSchema = z.object({
  tenantName: z.string().min(1),
  entraTenantId: z.string().min(1),
  tenantSettings: z.string(),
  adminEmail: z.string().email(),
  adminDisplayName: z.string().min(1),
  adminEntraObjectId: z.string().min(1),
})

export type SetupStatus = z.infer<typeof setupStatusSchema>
export type SetupPayload = z.infer<typeof setupPayloadSchema>
