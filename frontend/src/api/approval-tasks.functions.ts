import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import {
  pagedApprovalTaskListSchema,
  approvalTaskDetailSchema,
  approvalAuditEntrySchema,
} from './approval-tasks.schemas'
import { buildFilterParams } from './utils'

export const fetchApprovalTasks = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      status: z.string().optional(),
      type: z.string().optional(),
      search: z.string().optional(),
      showRead: z.boolean().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    })
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters, { pageSize: 25 })
    const data = await apiGet(`/approval-tasks?${params.toString()}`, context)
    return pagedApprovalTaskListSchema.parse(data)
  })

export const fetchApprovalTaskDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
      devicePage: z.number().optional(),
      devicePageSize: z.number().optional(),
      deviceVersion: z.string().optional(),
    })
  )
  .handler(async ({ context, data: { id, page, pageSize, devicePage, devicePageSize, deviceVersion } }) => {
    const params = new URLSearchParams()
    if (page) params.set('page', String(page))
    if (pageSize) params.set('pageSize', String(pageSize))
    if (devicePage) params.set('devicePage', String(devicePage))
    if (devicePageSize) params.set('devicePageSize', String(devicePageSize))
    if (deviceVersion !== undefined) params.set('deviceVersion', deviceVersion)
    const data = await apiGet(`/approval-tasks/${id}?${params.toString()}`, context)
    return approvalTaskDetailSchema.parse(data)
  })

export const fetchApprovalTaskPendingCount = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/approval-tasks/pending-count', context)
    return z.number().parse(data)
  })

export const resolveApprovalTask = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      action: z.enum(['approve', 'deny']),
      justification: z.string().optional(),
    })
  )
  .handler(async ({ context, data: { id, action, justification } }) => {
    await apiPost(`/approval-tasks/${id}/resolve`, context, { action, justification })
  })

export const markApprovalTaskRead = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiPost(`/approval-tasks/${id}/read`, context, {})
  })

export const fetchDecisionAuditTrail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantSoftwareId: z.string().uuid(),
      decisionId: z.string().uuid(),
    })
  )
  .handler(async ({ context, data: { tenantSoftwareId, decisionId } }) => {
    const data = await apiGet(
      `/software/${tenantSoftwareId}/remediation/decisions/${decisionId}/audit-trail`,
      context
    )
    return z.array(approvalAuditEntrySchema).parse(data)
  })
