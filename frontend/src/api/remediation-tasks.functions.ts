import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import {
  pagedRemediationTasksSchema,
  remediationTaskCreateResultSchema,
  remediationTaskTeamStatusSchema,
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
      taskId: z.string().uuid().optional(),
      deviceAssetId: z.string().uuid().optional(),
      caseId: z.string().uuid().optional(),
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
  .inputValidator(z.object({ caseId: z.string().uuid() }))
  .handler(async ({ context, data: { caseId } }) => {
    const data = await apiPost(`/remediation/cases/${caseId}/tasks`, context, {})
    return remediationTaskCreateResultSchema.parse(data)
  })

export const fetchRemediationTaskTeamStatuses = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ caseId: z.string().uuid() }))
  .handler(async ({ context, data: { caseId } }) => {
    const data = await apiGet(`/remediation/cases/${caseId}/team-statuses`, context)
    return z.array(remediationTaskTeamStatusSchema).parse(data)
  })
