import { z } from 'zod'

export const businessLabelSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  color: z.string().nullable(),
  isActive: z.boolean(),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const saveBusinessLabelSchema = z.object({
  name: z.string().trim().min(1),
  description: z.string().nullable().optional(),
  color: z.string().nullable().optional(),
  isActive: z.boolean().optional(),
})

export type BusinessLabel = z.infer<typeof businessLabelSchema>
export type SaveBusinessLabel = z.infer<typeof saveBusinessLabelSchema>
