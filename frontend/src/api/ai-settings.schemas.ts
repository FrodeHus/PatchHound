import { z } from 'zod'
import { nullableIsoDateTimeSchema } from './common.schemas'

export const tenantAiProfileSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  providerType: z.string(),
  isDefault: z.boolean(),
  isEnabled: z.boolean(),
  model: z.string(),
  systemPrompt: z.string(),
  temperature: z.number(),
  topP: z.number().nullable(),
  maxOutputTokens: z.number(),
  timeoutSeconds: z.number(),
  baseUrl: z.string(),
  deploymentName: z.string(),
  apiVersion: z.string(),
  keepAlive: z.string(),
  hasSecret: z.boolean(),
  lastValidatedAt: nullableIsoDateTimeSchema,
  lastValidationStatus: z.string(),
  lastValidationError: z.string(),
})

export const saveTenantAiProfileSchema = z.object({
  id: z.string().uuid().optional(),
  name: z.string().min(1),
  providerType: z.enum(['Ollama', 'AzureOpenAi', 'OpenAi']),
  isDefault: z.boolean(),
  isEnabled: z.boolean(),
  model: z.string().min(1),
  systemPrompt: z.string().min(1),
  temperature: z.number().min(0).max(2),
  topP: z.number().min(0).max(1).nullable(),
  maxOutputTokens: z.number().int().positive(),
  timeoutSeconds: z.number().int().positive(),
  baseUrl: z.string(),
  deploymentName: z.string(),
  apiVersion: z.string(),
  keepAlive: z.string(),
  apiKey: z.string(),
})

export const tenantAiProfileValidationSchema = z.object({
  id: z.string().uuid(),
  validationStatus: z.string(),
  validationError: z.string(),
  lastValidatedAt: nullableIsoDateTimeSchema,
})

export type TenantAiProfile = z.infer<typeof tenantAiProfileSchema>
export type SaveTenantAiProfile = z.infer<typeof saveTenantAiProfileSchema>
export type TenantAiProfileValidation = z.infer<typeof tenantAiProfileValidationSchema>
