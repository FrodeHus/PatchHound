import { useMutation, useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const setupStatusSchema = z.object({
  isInitialized: z.boolean(),
})

const setupPayloadSchema = z.object({
  tenantName: z.string().min(1),
  entraTenantId: z.string().min(1),
  tenantSettings: z.string(),
  adminEmail: z.string().email(),
  adminDisplayName: z.string().min(1),
  adminEntraObjectId: z.string().min(1),
})

export type SetupPayload = z.infer<typeof setupPayloadSchema>

export const setupKeys = {
  all: ['setup'] as const,
  status: () => [...setupKeys.all, 'status'] as const,
}

export function useSetupStatus() {
  return useQuery({
    queryKey: setupKeys.status(),
    queryFn: async () => {
      const response = await apiClient.get<unknown>('/setup/status')
      return setupStatusSchema.parse(response)
    },
    retry: false,
  })
}

export function useCompleteSetup() {
  return useMutation({
    mutationFn: async (payload: SetupPayload) => {
      const validPayload = setupPayloadSchema.parse(payload)
      await apiClient.post<null>('/setup/complete', validPayload)
    },
  })
}
