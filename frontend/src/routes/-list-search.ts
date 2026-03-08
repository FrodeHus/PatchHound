import { z } from 'zod'

const positiveInt = (fallback: number) => z.coerce.number().int().min(1).catch(fallback)

export const baseListSearchSchema = z.object({
  page: positiveInt(1),
  pageSize: positiveInt(25),
})

export const searchStringSchema = z.string().catch('')

export const searchBooleanSchema = z
  .union([z.boolean(), z.enum(['true', 'false'])])
  .transform((value) => value === true || value === 'true')
  .catch(false)
