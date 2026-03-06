import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'
import { auditLogSchema, pagedAuditLogSchema } from '@/api/useAuditLog'

const vulnerabilitySchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  vendorSeverity: z.string(),
  status: z.string(),
  source: z.string(),
  cvssScore: z.number().nullable(),
  publishedDate: z.string().datetime().nullable(),
  affectedAssetCount: z.number(),
  adjustedSeverity: z.string().nullable(),
})

const pagedVulnerabilitySchema = z.object({
  items: z.array(vulnerabilitySchema),
  totalCount: z.number(),
})

const affectedAssetSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  assetType: z.string(),
  status: z.string(),
  detectedDate: z.string().datetime(),
  resolvedDate: z.string().datetime().nullable(),
})

const orgSeveritySchema = z.object({
  adjustedSeverity: z.string(),
  justification: z.string(),
  assetCriticalityFactor: z.string().nullable(),
  exposureFactor: z.string().nullable(),
  compensatingControls: z.string().nullable(),
  adjustedAt: z.string().datetime(),
})

const vulnerabilityDetailSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  description: z.string(),
  vendorSeverity: z.string(),
  status: z.string(),
  source: z.string(),
  cvssScore: z.number().nullable(),
  cvssVector: z.string().nullable(),
  publishedDate: z.string().datetime().nullable(),
  affectedAssets: z.array(affectedAssetSchema),
  organizationalSeverity: orgSeveritySchema.nullable(),
})

const aiReportSchema = z.object({
  id: z.string().uuid(),
  vulnerabilityId: z.string().uuid(),
  content: z.string(),
  provider: z.string(),
  generatedAt: z.string().datetime(),
})

const commentSchema = z.object({
  id: z.string().uuid(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  authorId: z.string().uuid(),
  content: z.string(),
  createdAt: z.string().datetime(),
  updatedAt: z.string().datetime().nullable(),
})

export type Vulnerability = z.infer<typeof vulnerabilitySchema>
export type VulnerabilityDetail = z.infer<typeof vulnerabilityDetailSchema>
export type AffectedAsset = z.infer<typeof affectedAssetSchema>
export type AiReport = z.infer<typeof aiReportSchema>
export type CommentItem = z.infer<typeof commentSchema>
export type AuditLogItem = z.infer<typeof auditLogSchema>

export type VulnerabilityListFilters = {
  severity?: string
  status?: string
  source?: string
  search?: string
  page?: number
  pageSize?: number
}

export const vulnerabilityKeys = {
  all: ['vulnerabilities'] as const,
  lists: () => [...vulnerabilityKeys.all, 'list'] as const,
  list: (filters: VulnerabilityListFilters) => [...vulnerabilityKeys.lists(), filters] as const,
  details: () => [...vulnerabilityKeys.all, 'detail'] as const,
  detail: (id: string) => [...vulnerabilityKeys.details(), id] as const,
  comments: (id: string) => [...vulnerabilityKeys.detail(id), 'comments'] as const,
  timeline: (id: string) => [...vulnerabilityKeys.detail(id), 'timeline'] as const,
}

function buildListQuery(filters: VulnerabilityListFilters): string {
  const params = new URLSearchParams()
  if (filters.severity) params.set('severity', filters.severity)
  if (filters.status) params.set('status', filters.status)
  if (filters.source) params.set('source', filters.source)
  if (filters.search) params.set('search', filters.search)
  params.set('page', String(filters.page ?? 1))
  params.set('pageSize', String(filters.pageSize ?? 25))

  return `/vulnerabilities?${params.toString()}`
}

export function useVulnerabilities(filters: VulnerabilityListFilters) {
  return useQuery({
    queryKey: vulnerabilityKeys.list(filters),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(buildListQuery(filters))
      return pagedVulnerabilitySchema.parse(response)
    },
  })
}

export function useVulnerabilityDetail(id: string) {
  return useQuery({
    queryKey: vulnerabilityKeys.detail(id),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(`/vulnerabilities/${id}`)
      return vulnerabilityDetailSchema.parse(response)
    },
  })
}

export function useUpdateOrganizationalSeverity(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (payload: {
      adjustedSeverity: string
      justification: string
      assetCriticalityFactor?: string
      exposureFactor?: string
      compensatingControls?: string
    }) => {
      await apiClient.put<null>(`/vulnerabilities/${id}/organizational-severity`, payload)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: vulnerabilityKeys.detail(id) })
      await queryClient.invalidateQueries({ queryKey: vulnerabilityKeys.lists() })
    },
  })
}

export function useGenerateAiReport(id: string) {
  return useMutation({
    mutationFn: async (providerName: string) => {
      const response = await apiClient.post<unknown>(`/vulnerabilities/${id}/ai-report`, { providerName })
      return aiReportSchema.parse(response)
    },
  })
}

export function useVulnerabilityComments(id: string) {
  return useQuery({
    queryKey: vulnerabilityKeys.comments(id),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(`/vulnerabilities/${id}/comments`)
      return z.array(commentSchema).parse(response)
    },
  })
}

export function useAddVulnerabilityComment(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (content: string) => {
      const response = await apiClient.post<unknown>(`/vulnerabilities/${id}/comments`, { content })
      return commentSchema.parse(response)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: vulnerabilityKeys.comments(id) })
    },
  })
}

export function useVulnerabilityTimeline(id: string) {
  return useQuery({
    queryKey: vulnerabilityKeys.timeline(id),
    queryFn: async () => {
      const params = new URLSearchParams({
        entityType: 'Vulnerability',
        entityId: id,
        page: '1',
        pageSize: '50',
      })

      const response = await apiClient.get<unknown>(`/audit-log?${params.toString()}`)
      const parsed = pagedAuditLogSchema.parse(response)
      return parsed.items
    },
  })
}
