import { z } from 'zod'
import { isoDateTimeSchema, nullableIsoDateTimeSchema } from './common.schemas'

export const workNoteSchema = z.object({
  id: z.string().uuid(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  authorId: z.string().uuid(),
  authorDisplayName: z.string(),
  content: z.string(),
  createdAt: isoDateTimeSchema,
  updatedAt: nullableIsoDateTimeSchema,
  canEdit: z.boolean(),
  canDelete: z.boolean(),
})

export type WorkNote = z.infer<typeof workNoteSchema>
