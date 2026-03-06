import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const campaignSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  status: z.string(),
  createdAt: z.string().datetime(),
  vulnerabilityCount: z.number(),
  totalTasks: z.number(),
  completedTasks: z.number(),
})

const campaignDetailSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  status: z.string(),
  createdBy: z.string().uuid(),
  createdAt: z.string().datetime(),
  vulnerabilityCount: z.number(),
  totalTasks: z.number(),
  completedTasks: z.number(),
  vulnerabilityIds: z.array(z.string().uuid()),
})

const pagedCampaignSchema = z.object({
  items: z.array(campaignSchema),
  totalCount: z.number(),
})

export type Campaign = z.infer<typeof campaignSchema>
export type CampaignDetail = z.infer<typeof campaignDetailSchema>

export type CampaignFilters = {
  status?: string
  page?: number
  pageSize?: number
}

export const campaignKeys = {
  all: ['campaigns'] as const,
  list: (filters: CampaignFilters) => [...campaignKeys.all, 'list', filters] as const,
  detail: (id: string) => [...campaignKeys.all, 'detail', id] as const,
}

function buildQuery(filters: CampaignFilters): string {
  const params = new URLSearchParams()
  if (filters.status) params.set('status', filters.status)
  params.set('page', String(filters.page ?? 1))
  params.set('pageSize', String(filters.pageSize ?? 50))
  return `/campaigns?${params.toString()}`
}

export function useCampaigns(filters: CampaignFilters) {
  return useQuery({
    queryKey: campaignKeys.list(filters),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(buildQuery(filters))
      return pagedCampaignSchema.parse(response)
    },
  })
}

export function useCampaignDetail(id: string) {
  return useQuery({
    queryKey: campaignKeys.detail(id),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(`/campaigns/${id}`)
      return campaignDetailSchema.parse(response)
    },
  })
}

export function useCreateCampaign() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ name, description }: { name: string; description?: string }) => {
      const response = await apiClient.post<unknown>('/campaigns', {
        name,
        description: description ?? null,
      })
      return campaignDetailSchema.parse(response)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: campaignKeys.all })
    },
  })
}

export function useLinkCampaignVulnerabilities(campaignId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (vulnerabilityIds: string[]) => {
      await apiClient.post<null>(`/campaigns/${campaignId}/vulnerabilities`, { vulnerabilityIds })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: campaignKeys.detail(campaignId) })
    },
  })
}

export function useBulkAssignCampaign(campaignId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (assigneeId: string) => {
      await apiClient.post<unknown>(`/campaigns/${campaignId}/bulk-assign`, { assigneeId })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: campaignKeys.detail(campaignId) })
      await queryClient.invalidateQueries({ queryKey: campaignKeys.list({}) })
    },
  })
}
