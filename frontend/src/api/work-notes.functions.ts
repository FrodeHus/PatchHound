import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { workNoteSchema } from './work-notes.schemas'

const entityTypeSchema = z.enum(['vulnerabilities', 'software', 'remediations', 'assets', 'devices'])

export const fetchWorkNotes = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    entityType: entityTypeSchema,
    entityId: z.string().uuid(),
  }))
  .handler(async ({ context, data: { entityType, entityId } }) => {
    const data = await apiGet(`/work-notes/${entityType}/${entityId}`, context)
    return z.array(workNoteSchema).parse(data)
  })

export const createWorkNote = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    entityType: entityTypeSchema,
    entityId: z.string().uuid(),
    content: z.string(),
  }))
  .handler(async ({ context, data: { entityType, entityId, content } }) => {
    const data = await apiPost(`/work-notes/${entityType}/${entityId}`, context, { content })
    return workNoteSchema.parse(data)
  })

export const updateWorkNote = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    noteId: z.string().uuid(),
    content: z.string(),
  }))
  .handler(async ({ context, data: { noteId, content } }) => {
    const data = await apiPut(`/work-notes/${noteId}`, context, { content })
    return workNoteSchema.parse(data)
  })

export const deleteWorkNote = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    noteId: z.string().uuid(),
  }))
  .handler(async ({ context, data: { noteId } }) => {
    await apiDelete(`/work-notes/${noteId}`, context)
  })
