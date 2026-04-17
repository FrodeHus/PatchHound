import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { pagedCloudApplicationsSchema } from './cloud-applications.schemas'

export const fetchCloudApplications = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      search: z.string().optional(),
      credentialFilter: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams()
    if (data.search) params.set('search', data.search)
    if (data.credentialFilter) params.set('credentialFilter', data.credentialFilter)
    params.set('page', String(data.page ?? 1))
    params.set('pageSize', String(data.pageSize ?? 25))
    const result = await apiGet(`/cloud-applications?${params}`, context)
    return pagedCloudApplicationsSchema.parse(result)
  })
