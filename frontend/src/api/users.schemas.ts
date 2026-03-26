import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const userTeamMembershipSchema = z.object({
  teamId: z.string().uuid(),
  teamName: z.string(),
  isDefault: z.boolean(),
})

export const userListItemSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  email: z.string().email(),
  displayName: z.string(),
  company: z.string().nullable(),
  isEnabled: z.boolean(),
  roles: z.array(z.string()),
  teams: z.array(userTeamMembershipSchema),
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
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  email: z.string().email(),
  displayName: z.string(),
  company: z.string().nullable(),
  isEnabled: z.boolean(),
  entraObjectId: z.string(),
  roles: z.array(z.string()),
  teams: z.array(userTeamMembershipSchema),
  recentAudit: z.array(userAuditItemSchema),
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
