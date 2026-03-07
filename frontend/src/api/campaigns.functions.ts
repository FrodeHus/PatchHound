import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import { pagedCampaignSchema, campaignDetailSchema } from './campaigns.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'

export const fetchCampaigns = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      status: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/campaigns?${params.toString()}`, context.token)
    return pagedCampaignSchema.parse(data)
  })

export const fetchCampaignDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/campaigns/${id}`, context.token)
    return campaignDetailSchema.parse(data)
  })

export const createCampaign = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ name: z.string(), description: z.string().optional() }))
  .handler(async ({ context, data: { name, description } }) => {
    const data = await apiPost('/campaigns', context.token, {
      name,
      description: description ?? null,
    })
    return campaignDetailSchema.parse(data)
  })

export const linkCampaignVulnerabilities = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ campaignId: z.string(), vulnerabilityIds: z.array(z.string()) }))
  .handler(async ({ context, data: { campaignId, vulnerabilityIds } }) => {
    await apiPost(`/campaigns/${campaignId}/vulnerabilities`, context.token, { vulnerabilityIds })
  })

export const bulkAssignCampaign = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ campaignId: z.string(), assigneeId: z.string() }))
  .handler(async ({ context, data: { campaignId, assigneeId } }) => {
    await apiPost(`/campaigns/${campaignId}/bulk-assign`, context.token, { assigneeId })
  })
