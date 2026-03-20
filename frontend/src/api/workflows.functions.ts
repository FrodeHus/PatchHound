import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import {
  pagedWorkflowDefinitionsSchema,
  workflowDefinitionDetailSchema,
  workflowInstanceSchema,
  pagedWorkflowInstancesSchema,
  workflowInstanceDetailSchema,
  pagedWorkflowActionsSchema,
} from './workflows.schemas'
import { buildFilterParams } from './utils'
import { z } from 'zod'

// ─── Definitions ─────────────────────────────────────────

export const fetchWorkflowDefinitions = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantId: z.string().optional(),
      scope: z.string().optional(),
      status: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/workflows/definitions?${params.toString()}`, context)
    return pagedWorkflowDefinitionsSchema.parse(data)
  })

export const fetchWorkflowDefinition = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/workflows/definitions/${id}`, context)
    return workflowDefinitionDetailSchema.parse(data)
  })

export const createWorkflowDefinition = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      tenantId: z.string().uuid().nullable().optional(),
      name: z.string(),
      description: z.string().nullable().optional(),
      scope: z.string(),
      triggerType: z.string(),
      graphJson: z.string(),
    }),
  )
  .handler(async ({ context, data: payload }) => {
    const data = await apiPost('/workflows/definitions', context, payload)
    return workflowDefinitionDetailSchema.parse(data)
  })

export const updateWorkflowDefinition = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string(),
      name: z.string(),
      description: z.string().nullable().optional(),
      graphJson: z.string(),
    }),
  )
  .handler(async ({ context, data: { id, ...payload } }) => {
    const data = await apiPut(`/workflows/definitions/${id}`, context, payload)
    return workflowDefinitionDetailSchema.parse(data)
  })

export const publishWorkflowDefinition = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    await apiPost(`/workflows/definitions/${id}/publish`, context)
  })

export const archiveWorkflowDefinition = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    await apiPost(`/workflows/definitions/${id}/archive`, context)
  })

export const deleteWorkflowDefinition = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/workflows/definitions/${id}`, context)
  })

export const runWorkflowDefinition = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string(), contextJson: z.string().optional() }))
  .handler(async ({ context, data: { id, contextJson } }) => {
    const data = await apiPost(`/workflows/definitions/${id}/run`, context, { contextJson })
    return workflowInstanceSchema.parse(data)
  })

// ─── Instances ───────────────────────────────────────────

export const fetchWorkflowInstances = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      definitionId: z.string().optional(),
      tenantId: z.string().optional(),
      status: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/workflows/instances?${params.toString()}`, context)
    return pagedWorkflowInstancesSchema.parse(data)
  })

export const fetchWorkflowInstance = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    const data = await apiGet(`/workflows/instances/${id}`, context)
    return workflowInstanceDetailSchema.parse(data)
  })

export const cancelWorkflowInstance = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string() }))
  .handler(async ({ context, data: { id } }) => {
    await apiPost(`/workflows/instances/${id}/cancel`, context)
  })

// ─── Actions ─────────────────────────────────────────────

export const fetchWorkflowActions = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      teamId: z.string().optional(),
      status: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/workflows/actions?${params.toString()}`, context)
    return pagedWorkflowActionsSchema.parse(data)
  })

export const fetchMyWorkflowActions = createServerFn({ method: 'GET' })
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
    const data = await apiGet(`/workflows/actions/mine?${params.toString()}`, context)
    return pagedWorkflowActionsSchema.parse(data)
  })

export const completeWorkflowAction = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string(), responseJson: z.string().nullable().optional() }))
  .handler(async ({ context, data: { id, responseJson } }) => {
    await apiPost(`/workflows/actions/${id}/complete`, context, { responseJson })
  })

export const rejectWorkflowAction = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string(), responseJson: z.string().nullable().optional() }))
  .handler(async ({ context, data: { id, responseJson } }) => {
    await apiPost(`/workflows/actions/${id}/reject`, context, { responseJson })
  })
