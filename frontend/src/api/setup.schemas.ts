import { z } from 'zod'

export const setupStatusSchema = z.object({
  isInitialized: z.boolean(),
  requiresSetup: z.boolean(),
})

export const setupContextSchema = z.object({
  tenantName: z.string().min(1),
  entraTenantId: z.string().min(1),
  adminEmail: z.string().email(),
  adminDisplayName: z.string().min(1),
  adminEntraObjectId: z.string().min(1),
})

export const setupPayloadSchema = z.object({})

export type SetupStatus = z.infer<typeof setupStatusSchema>
export type SetupContext = z.infer<typeof setupContextSchema>
export type SetupPayload = z.infer<typeof setupPayloadSchema>
