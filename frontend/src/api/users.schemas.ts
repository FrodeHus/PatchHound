import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const userTeamMembershipSchema = z.object({
  teamId: z.string().uuid(),
  teamName: z.string(),
  isDefault: z.boolean(),
})

export const userTenantAccessSchema = z.object({
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  roles: z.array(z.string()),
})

export const userListItemSchema = z.object({
  id: z.string().uuid(),
  email: z.string().email(),
  displayName: z.string(),
  company: z.string().nullable(),
  isEnabled: z.boolean(),
  accessScope: z.string(),
  roles: z.array(z.string()),
  teams: z.array(userTeamMembershipSchema),
  tenantNames: z.array(z.string()),
})

export const userAuditItemSchema = z.object({
  id: z.string().uuid(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  action: z.string(),
  summary: z.string().nullable(),
  userDisplayName: z.string().nullable(),
  timestamp: isoDateTimeSchema,
})

export const userDetailSchema = z.object({
  id: z.string().uuid(),
  email: z.string().email(),
  displayName: z.string(),
  company: z.string().nullable(),
  isEnabled: z.boolean(),
  entraObjectId: z.string(),
  accessScope: z.string(),
  currentTenantId: z.string().uuid().nullable(),
  currentTenantName: z.string().nullable(),
  roles: z.array(z.string()),
  teams: z.array(userTeamMembershipSchema),
  recentAudit: z.array(userAuditItemSchema),
  tenantAccess: z.array(userTenantAccessSchema),
})

export const pagedUsersSchema = pagedResponseMetaSchema.extend({
  items: z.array(userListItemSchema),
})

export const pagedUserAuditSchema = pagedResponseMetaSchema.extend({
  items: z.array(userAuditItemSchema),
})

export type UserListItem = z.infer<typeof userListItemSchema>
export type UserDetail = z.infer<typeof userDetailSchema>
export type UserAuditItem = z.infer<typeof userAuditItemSchema>
