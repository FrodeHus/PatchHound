import { useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

export const auditLogSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  action: z.string(),
  oldValues: z.string().nullable(),
  newValues: z.string().nullable(),
  userId: z.string().uuid(),
  timestamp: z.string().datetime(),
})

export const pagedAuditLogSchema = z.object({
  items: z.array(auditLogSchema),
  totalCount: z.number(),
})

export type AuditLogItem = z.infer<typeof auditLogSchema>

export type AuditLogFilters = {
  entityType?: string
  action?: string
  page?: number
  pageSize?: number
}

export const auditKeys = {
  all: ['audit-log'] as const,
  list: (filters: AuditLogFilters) => [...auditKeys.all, 'list', filters] as const,
}

function buildQuery(filters: AuditLogFilters): string {
  const params = new URLSearchParams()
  if (filters.entityType) params.set('entityType', filters.entityType)
  if (filters.action) params.set('action', filters.action)
  params.set('page', String(filters.page ?? 1))
  params.set('pageSize', String(filters.pageSize ?? 50))
  return `/audit-log?${params.toString()}`
}

export function useAuditLog(filters: AuditLogFilters) {
  return useQuery({
    queryKey: auditKeys.list(filters),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(buildQuery(filters))
      return pagedAuditLogSchema.parse(response)
    },
  })
}
