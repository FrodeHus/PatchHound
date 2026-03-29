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
  appClientId: z.string(),
  adminConsentUrl: z.string().url().nullable(),
})

export const setupPayloadSchema = z
  .object({
    tenantName: z.string().trim().min(1, 'Tenant name is required'),
    defender: z.object({
      enabled: z.boolean(),
      clientId: z.string().trim(),
      clientSecret: z.string(),
    }),
  })
  .superRefine((payload, ctx) => {
    if (!payload.defender.enabled) {
      return
    }

    if (!payload.defender.clientId) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['defender', 'clientId'],
        message: 'Client ID is required when Defender setup is enabled',
      })
    }

    if (!payload.defender.clientSecret.trim()) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['defender', 'clientSecret'],
        message: 'Client secret is required when Defender setup is enabled',
      })
    }
  })

export type SetupStatus = z.infer<typeof setupStatusSchema>
export type SetupContext = z.infer<typeof setupContextSchema>
export type SetupPayload = z.infer<typeof setupPayloadSchema>
