import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const userRoleSchema = z.object({
  tenantId: z.string().uuid(),
  tenantName: z.string(),
  role: z.string(),
})

export const userSchema = z.object({
  id: z.string().uuid(),
  email: z.string().email(),
  displayName: z.string(),
  roles: z.array(userRoleSchema),
})

export const pagedUsersSchema = pagedResponseMetaSchema.extend({
  items: z.array(userSchema),
})

export type UserItem = z.infer<typeof userSchema>
