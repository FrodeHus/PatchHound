import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import {
  pagedRemediationTasksSchema,
  remediationTaskCreateResultSchema,
} from './remediation-tasks.schemas'
import { buildFilterParams } from './utils'

export const fetchRemediationTasks = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      search: z.string().optional(),
      vendor: z.string().optional(),
      criticality: z.string().optional(),
      assetOwner: z.string().optional(),
      deviceAssetId: z.string().uuid().optional(),
      tenantSoftwareId: z.string().uuid().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters, { pageSize: 25 })
    const data = await apiGet(`/remediation/tasks?${params.toString()}`, context)
    return pagedRemediationTasksSchema.parse(data)
  })

export const createRemediationTasksForSoftware = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ tenantSoftwareId: z.string().uuid() }))
  .handler(async ({ context, data: { tenantSoftwareId } }) => {
    const data = await apiPost(`/remediation/tasks/software/${tenantSoftwareId}`, context, {})
    return remediationTaskCreateResultSchema.parse(data)
  })
