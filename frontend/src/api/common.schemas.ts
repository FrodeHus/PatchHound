import { z } from 'zod'

export const isoDateTimeSchema = z.string().refine((value) => !Number.isNaN(Date.parse(value)), {
  message: 'Invalid datetime',
})

export const nullableIsoDateTimeSchema = z.string().nullable().refine(
  (value) => value === null || !Number.isNaN(Date.parse(value)),
  { message: 'Invalid date' },
)
