import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import { pagedTasksSchema } from './tasks.schemas'
import { z } from 'zod'

export const fetchTasks = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      status: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = new URLSearchParams()
    if (filters.status) params.set('status', filters.status)
    params.set('page', String(filters.page ?? 1))
    params.set('pageSize', String(filters.pageSize ?? 50))

    const data = await apiGet(`/tasks?${params.toString()}`, context.token)
    return pagedTasksSchema.parse(data)
  })

export const updateTaskStatus = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string(),
      status: z.string(),
      justification: z.string().optional(),
    }),
  )
  .handler(async ({ context, data: { id, status, justification } }) => {
    await apiPut(`/tasks/${id}/status`, context.token, { status, justification })
  })
