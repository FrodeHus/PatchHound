import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { pagedUsersSchema } from './users.schemas'
import { z } from 'zod'

export const fetchUsers = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .validator(
    z.object({
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const page = filters.page ?? 1
    const pageSize = filters.pageSize ?? 50
    const data = await apiGet(`/users?page=${page}&pageSize=${pageSize}`, context.token)
    return pagedUsersSchema.parse(data)
  })

export const inviteUser = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .validator(
    z.object({
      email: z.string(),
      displayName: z.string(),
      entraObjectId: z.string(),
    }),
  )
  .handler(async ({ context, data: payload }) => {
    await apiPost('/users/invite', context.token, payload)
  })

export const updateUserRoles = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .validator(
    z.object({
      userId: z.string(),
      roles: z.array(z.object({ tenantId: z.string(), role: z.string() })),
    }),
  )
  .handler(async ({ context, data: { userId, roles } }) => {
    await apiPut(`/users/${userId}/roles`, context.token, { roles })
  })
