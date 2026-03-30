import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { pagedUserAuditSchema, pagedUsersSchema, userDetailSchema } from './users.schemas'
import { buildFilterParams } from './utils'

export const fetchUsers = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      search: z.string().optional(),
      role: z.string().optional(),
      status: z.string().optional(),
      teamId: z.string().uuid().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/users?${params.toString()}`, context)
    return pagedUsersSchema.parse(data)
  })

export const fetchUserDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ userId: z.string().uuid() }))
  .handler(async ({ context, data: { userId } }) => {
    const data = await apiGet(`/users/${userId}`, context)
    return userDetailSchema.parse(data)
  })

export const fetchUserAudit = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      userId: z.string().uuid(),
      entityType: z.string().optional(),
      action: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: { userId, ...filters } }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/users/${userId}/audit?${params.toString()}`, context)
    return pagedUserAuditSchema.parse(data)
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
    await apiPost('/users/invite', context, payload)
  })

export const updateUser = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      userId: z.string().uuid(),
      displayName: z.string(),
      email: z.string().email(),
      company: z.string().nullable(),
      isEnabled: z.boolean(),
      accessScope: z.string(),
      roles: z.array(z.string()),
      teamIds: z.array(z.string().uuid()),
      tenantAccess: z.array(z.object({
        tenantId: z.string().uuid(),
        roles: z.array(z.string()),
      })),
    }),
  )
  .handler(async ({ context, data: { userId, ...payload } }) => {
    await apiPut(`/users/${userId}`, context, payload)
  })
