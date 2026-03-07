import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import { pagedTeamsSchema } from './teams.schemas'
import { z } from 'zod'

export const fetchTeams = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .validator(
    z.object({
      tenantId: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = new URLSearchParams()
    if (filters.tenantId) params.set('tenantId', filters.tenantId)
    params.set('page', String(filters.page ?? 1))
    params.set('pageSize', String(filters.pageSize ?? 50))

    const data = await apiGet(`/teams?${params.toString()}`, context.token)
    return pagedTeamsSchema.parse(data)
  })

export const createTeam = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .validator(z.object({ name: z.string(), tenantId: z.string() }))
  .handler(async ({ context, data: payload }) => {
    await apiPost('/teams', context.token, payload)
  })
