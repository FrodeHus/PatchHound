import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { pagedTeamsSchema, teamDetailSchema, teamMembershipRulePreviewSchema } from './teams.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'
import { filterNodeSchema } from './asset-rules.schemas'

export const fetchTeams = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantId: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/teams?${params.toString()}`, context)
    return pagedTeamsSchema.parse(data)
  })

export const createTeam = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ name: z.string(), tenantId: z.string() }))
  .handler(async ({ context, data: payload }) => {
    await apiPost('/teams', context, payload)
  })

export const fetchTeamDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ teamId: z.string() }))
  .handler(async ({ context, data: { teamId } }) => {
    const data = await apiGet(`/teams/${teamId}`, context)
    return teamDetailSchema.parse(data)
  })

export const updateTeamMembers = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      teamId: z.string().uuid(),
      userId: z.string().uuid(),
      action: z.enum(['add', 'remove']),
    }),
  )
  .handler(async ({ context, data: { teamId, ...payload } }) => {
    await apiPut(`/teams/${teamId}/members`, context, payload)
  })

export const updateTeamMembershipRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      teamId: z.string().uuid(),
      isDynamic: z.boolean(),
      acknowledgeMemberReset: z.boolean(),
      filterDefinition: filterNodeSchema,
    }),
  )
  .handler(async ({ context, data: { teamId, ...payload } }) => {
    await apiPut(`/teams/${teamId}/rule`, context, payload)
  })

export const previewTeamMembershipRule = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      teamId: z.string().uuid(),
      isDynamic: z.boolean(),
      acknowledgeMemberReset: z.boolean(),
      filterDefinition: filterNodeSchema,
    }),
  )
  .handler(async ({ context, data: { teamId, ...payload } }) => {
    const data = await apiPost(`/teams/${teamId}/rule/preview`, context, payload)
    return teamMembershipRulePreviewSchema.parse(data)
  })
