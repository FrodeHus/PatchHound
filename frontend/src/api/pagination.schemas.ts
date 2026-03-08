import { z } from 'zod'

export const pagedResponseMetaSchema = z.object({
  totalCount: z.number(),
  page: z.number(),
  pageSize: z.number(),
  totalPages: z.number(),
})
