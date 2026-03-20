import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const workflowDefinitionSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid().nullable(),
  name: z.string(),
  description: z.string().nullable(),
  scope: z.string(),
  triggerType: z.string(),
  version: z.number(),
  status: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const workflowDefinitionDetailSchema = workflowDefinitionSchema.extend({
  graphJson: z.string(),
  createdBy: z.string().uuid(),
})

export const pagedWorkflowDefinitionsSchema = pagedResponseMetaSchema.extend({
  items: z.array(workflowDefinitionSchema),
})

export const workflowNodeExecutionSchema = z.object({
  id: z.string().uuid(),
  nodeId: z.string(),
  nodeType: z.string(),
  status: z.string(),
  inputJson: z.string().nullable(),
  outputJson: z.string().nullable(),
  error: z.string().nullable(),
  startedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  assignedTeamId: z.string().uuid().nullable(),
  completedByUserId: z.string().uuid().nullable(),
})

export const workflowInstanceSchema = z.object({
  id: z.string().uuid(),
  workflowDefinitionId: z.string().uuid(),
  workflowName: z.string(),
  definitionVersion: z.number(),
  tenantId: z.string().uuid().nullable(),
  triggerType: z.string(),
  status: z.string(),
  startedAt: z.string(),
  completedAt: z.string().nullable(),
  error: z.string().nullable(),
})

export const workflowInstanceDetailSchema = workflowInstanceSchema.extend({
  contextJson: z.string(),
  nodeExecutions: z.array(workflowNodeExecutionSchema),
})

export const pagedWorkflowInstancesSchema = pagedResponseMetaSchema.extend({
  items: z.array(workflowInstanceSchema),
})

export const workflowActionSchema = z.object({
  id: z.string().uuid(),
  workflowInstanceId: z.string().uuid(),
  nodeExecutionId: z.string().uuid(),
  tenantId: z.string().uuid(),
  teamId: z.string().uuid(),
  actionType: z.string(),
  instructions: z.string().nullable(),
  status: z.string(),
  responseJson: z.string().nullable(),
  dueAt: z.string().nullable(),
  createdAt: z.string(),
  completedAt: z.string().nullable(),
  completedByUserId: z.string().uuid().nullable(),
})

export const pagedWorkflowActionsSchema = pagedResponseMetaSchema.extend({
  items: z.array(workflowActionSchema),
})

export type WorkflowDefinitionItem = z.infer<typeof workflowDefinitionSchema>
export type WorkflowDefinitionDetail = z.infer<typeof workflowDefinitionDetailSchema>
export type WorkflowInstanceItem = z.infer<typeof workflowInstanceSchema>
export type WorkflowInstanceDetail = z.infer<typeof workflowInstanceDetailSchema>
export type WorkflowActionItem = z.infer<typeof workflowActionSchema>
