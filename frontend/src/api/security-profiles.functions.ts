import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { buildFilterParams } from './utils'
import { pagedSecurityProfilesSchema } from './security-profiles.schemas'

export const fetchSecurityProfiles = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/security-profiles?${params.toString()}`, context)
    return pagedSecurityProfilesSchema.parse(data)
  })

export const createSecurityProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string(),
      description: z.string().optional(),
      environmentClass: z.string(),
      internetReachability: z.string(),
      confidentialityRequirement: z.string(),
      integrityRequirement: z.string(),
      availabilityRequirement: z.string(),
    }),
  )
  .handler(async ({ context, data: { name, description, environmentClass, internetReachability, confidentialityRequirement, integrityRequirement, availabilityRequirement } }) => {
    await apiPost('/security-profiles', context, {
      name,
      description,
      environmentClass,
      internetReachability,
      confidentialityRequirement,
      integrityRequirement,
      availabilityRequirement,
    })
  })

export const updateSecurityProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string(),
      description: z.string().optional(),
      environmentClass: z.string(),
      internetReachability: z.string(),
      confidentialityRequirement: z.string(),
      integrityRequirement: z.string(),
      availabilityRequirement: z.string(),
    }),
  )
  .handler(async ({ context, data: { id, name, description, environmentClass, internetReachability, confidentialityRequirement, integrityRequirement, availabilityRequirement } }) => {
    await apiPut(`/security-profiles/${id}`, context, {
      name,
      description,
      environmentClass,
      internetReachability,
      confidentialityRequirement,
      integrityRequirement,
      availabilityRequirement,
    })
  })
