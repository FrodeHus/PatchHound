import { z } from 'zod'

export const storedCredentialSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  type: z.string(),
  typeDisplayName: z.string(),
  isGlobal: z.boolean(),
  credentialTenantId: z.string(),
  clientId: z.string(),
  tenantIds: z.array(z.string().uuid()),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const createStoredCredentialSchema = z.object({
  name: z.string().min(1),
  type: z.string().min(1),
  isGlobal: z.boolean(),
  credentialTenantId: z.string(),
  clientId: z.string(),
  clientSecret: z.string().min(1),
  tenantIds: z.array(z.string().uuid()),
})

export const updateStoredCredentialSchema = z.object({
  id: z.string().uuid(),
  name: z.string().min(1),
  isGlobal: z.boolean(),
  credentialTenantId: z.string(),
  clientId: z.string(),
  clientSecret: z.string().nullable().optional(),
  tenantIds: z.array(z.string().uuid()),
})

export type StoredCredential = z.infer<typeof storedCredentialSchema>
export type CreateStoredCredentialInput = z.infer<typeof createStoredCredentialSchema>
export type UpdateStoredCredentialInput = z.infer<typeof updateStoredCredentialSchema>
