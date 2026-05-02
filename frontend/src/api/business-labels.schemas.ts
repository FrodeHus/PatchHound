import { z } from 'zod'

export const businessLabelWeightCategorySchema = z.enum([
  'Informational',
  'Normal',
  'Sensitive',
  'Critical',
])

export type BusinessLabelWeightCategory = z.infer<typeof businessLabelWeightCategorySchema>

export const WEIGHT_CATEGORY_CONFIG: Record<
  BusinessLabelWeightCategory,
  { label: string; description: string; riskWeight: number }
> = {
  Informational: {
    label: 'Informational',
    description: 'Low business value. Reduces risk score (0.5×).',
    riskWeight: 0.5,
  },
  Normal: {
    label: 'Normal',
    description: 'Standard business value. No score adjustment (1.0×).',
    riskWeight: 1.0,
  },
  Sensitive: {
    label: 'Sensitive',
    description: 'Elevated business value. Increases risk score (1.5×).',
    riskWeight: 1.5,
  },
  Critical: {
    label: 'Critical',
    description: 'Critical business value. Significantly increases risk score (2.0×).',
    riskWeight: 2.0,
  },
}

export const businessLabelSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  color: z.string().nullable(),
  isActive: z.boolean(),
  weightCategory: businessLabelWeightCategorySchema,
  riskWeight: z.number(),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const saveBusinessLabelSchema = z.object({
  name: z.string().trim().min(1),
  description: z.string().nullable().optional(),
  color: z.string().nullable().optional(),
  isActive: z.boolean().optional(),
  weightCategory: businessLabelWeightCategorySchema.optional(),
})

export type BusinessLabel = z.infer<typeof businessLabelSchema>
export type SaveBusinessLabel = z.infer<typeof saveBusinessLabelSchema>
