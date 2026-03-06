import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const userRoleSchema = z.object({
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  role: z.string(),
})

const userSchema = z.object({
  id: z.string().uuid(),
  email: z.string().email(),
  displayName: z.string(),
  roles: z.array(userRoleSchema),
})

const pagedUsersSchema = z.object({
  items: z.array(userSchema),
  totalCount: z.number(),
})

export type UserItem = z.infer<typeof userSchema>

export const userKeys = {
  all: ['users'] as const,
  list: (page: number, pageSize: number) => [...userKeys.all, 'list', page, pageSize] as const,
}

export function useUsers(page = 1, pageSize = 50) {
  return useQuery({
    queryKey: userKeys.list(page, pageSize),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(`/users?page=${page}&pageSize=${pageSize}`)
      return pagedUsersSchema.parse(response)
    },
  })
}

export function useInviteUser() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (payload: { email: string; displayName: string; entraObjectId: string }) => {
      await apiClient.post<unknown>('/users/invite', payload)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: userKeys.all })
    },
  })
}

export function useUpdateUserRoles() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      userId,
      roles,
    }: {
      userId: string
      roles: Array<{ tenantId: string; role: string }>
    }) => {
      await apiClient.put<null>(`/users/${userId}/roles`, { roles })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: userKeys.all })
    },
  })
}
