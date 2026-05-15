import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { myTasksPageSchema } from './my-tasks.schemas'
import { buildFilterParams } from './utils'

export const fetchMyTasks = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      pageSize: z.number().optional(),
      recommendationPage: z.number().optional(),
      decisionPage: z.number().optional(),
      approvalPage: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters, { pageSize: 25 })
    const data = await apiGet(`/my-tasks?${params.toString()}`, context)
    return myTasksPageSchema.parse(data)
  })
