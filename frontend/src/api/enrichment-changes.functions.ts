import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import {
  pagedEnrichmentChangeSchema,
} from './enrichment-changes.schemas'
import { z } from 'zod'

export const fetchEnrichmentChanges = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      entityType: z.string(),
      entityId: z.string().uuid(),
      sourceKey: z.string().optional(),
      fieldPath: z.string().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams({
      entityType: data.entityType,
      entityId: data.entityId,
      page: String(data.page ?? 1),
      pageSize: String(data.pageSize ?? 20),
    })

    if (data.sourceKey) params.set('sourceKey', data.sourceKey)
    if (data.fieldPath) params.set('fieldPath', data.fieldPath)

    const response = await apiGet(`/enrichment-changes?${params.toString()}`, context)
    return pagedEnrichmentChangeSchema.parse(response)
  })
