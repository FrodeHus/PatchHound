import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const tenantSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  entraTenantId: z.string(),
  settings: z.string(),
})

const pagedTenantSchema = z.object({
  items: z.array(tenantSchema),
  totalCount: z.number(),
})

export type TenantItem = z.infer<typeof tenantSchema>

export const settingsKeys = {
  all: ['settings'] as const,
  tenants: () => [...settingsKeys.all, 'tenants'] as const,
}

export function useTenants(page = 1, pageSize = 100) {
  return useQuery({
    queryKey: [...settingsKeys.tenants(), page, pageSize] as const,
    queryFn: async () => {
      const response = await apiClient.get<unknown>(`/tenants?page=${page}&pageSize=${pageSize}`)
      return pagedTenantSchema.parse(response)
    },
  })
}

export function useUpdateTenantSettings() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ tenantId, settings }: { tenantId: string; settings: string }) => {
      await apiClient.put<null>(`/tenants/${tenantId}/settings`, { settings })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: settingsKeys.tenants() })
    },
  })
}
