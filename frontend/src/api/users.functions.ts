import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { pagedUsersSchema } from './users.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'

export const fetchUsers = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/users?${params.toString()}`, context.token)
    return pagedUsersSchema.parse(data)
  })

export const inviteUser = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      email: z.string().email(),
      displayName: z.string(),
      entraObjectId: z.string(),
    }),
  )
  .handler(async ({ context, data: payload }) => {
    await apiPost('/users/invite', context.token, payload)
  })

export const updateUserRoles = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      userId: z.string(),
      roles: z.array(z.object({ tenantId: z.string(), role: z.string() })),
    }),
  )
  .handler(async ({ context, data: { userId, roles } }) => {
    await apiPut(`/users/${userId}/roles`, context.token, { roles })
  })
