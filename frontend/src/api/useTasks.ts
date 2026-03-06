import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const remediationTaskSchema = z.object({
  id: z.string().uuid(),
  vulnerabilityId: z.string().uuid(),
  assetId: z.string().uuid(),
  vulnerabilityTitle: z.string(),
  assetName: z.string(),
  status: z.string(),
  justification: z.string().nullable(),
  dueDate: z.string().datetime(),
  createdAt: z.string().datetime(),
  isOverdue: z.boolean(),
})

const pagedTasksSchema = z.object({
  items: z.array(remediationTaskSchema),
  totalCount: z.number(),
})

export type RemediationTask = z.infer<typeof remediationTaskSchema>

export type TaskFilters = {
  status?: string
  page?: number
  pageSize?: number
}

export const taskKeys = {
  all: ['tasks'] as const,
  list: (filters: TaskFilters) => [...taskKeys.all, 'list', filters] as const,
}

function buildTaskQuery(filters: TaskFilters): string {
  const params = new URLSearchParams()
  if (filters.status) params.set('status', filters.status)
  params.set('page', String(filters.page ?? 1))
  params.set('pageSize', String(filters.pageSize ?? 50))
  return `/tasks?${params.toString()}`
}

export function useTasks(filters: TaskFilters = {}) {
  return useQuery({
    queryKey: taskKeys.list(filters),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(buildTaskQuery(filters))
      return pagedTasksSchema.parse(response)
    },
  })
}

export function useUpdateTaskStatus() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      id,
      status,
      justification,
    }: {
      id: string
      status: string
      justification?: string
    }) => {
      await apiClient.put<null>(`/tasks/${id}/status`, { status, justification })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: taskKeys.all })
    },
  })
}
