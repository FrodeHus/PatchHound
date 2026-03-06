import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const teamSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  memberCount: z.number(),
})

const pagedTeamsSchema = z.object({
  items: z.array(teamSchema),
  totalCount: z.number(),
})

export type TeamItem = z.infer<typeof teamSchema>

export const teamKeys = {
  all: ['teams'] as const,
  list: (tenantId?: string, page = 1, pageSize = 50) => [...teamKeys.all, 'list', tenantId ?? 'all', page, pageSize] as const,
}

export function useTeams(tenantId?: string, page = 1, pageSize = 50) {
  return useQuery({
    queryKey: teamKeys.list(tenantId, page, pageSize),
    queryFn: async () => {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
      if (tenantId) params.set('tenantId', tenantId)
      const response = await apiClient.get<unknown>(`/teams?${params.toString()}`)
      return pagedTeamsSchema.parse(response)
    },
  })
}

export function useCreateTeam() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (payload: { name: string; tenantId: string }) => {
      await apiClient.post<unknown>('/teams', payload)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: teamKeys.all })
    },
  })
}
