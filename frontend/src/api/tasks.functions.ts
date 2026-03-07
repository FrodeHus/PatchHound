import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import { pagedTasksSchema } from './tasks.schemas'
import { buildFilterParams } from './utils'
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
    const params = buildFilterParams(filters)
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
