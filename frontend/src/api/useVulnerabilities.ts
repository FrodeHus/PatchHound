import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  addVulnerabilityComment,
  fetchVulnerabilityComments,
  fetchVulnerabilityTimeline,
  generateAiReport,
  updateOrganizationalSeverity,
} from './vulnerabilities.functions'

export type { AffectedAsset, CommentItem, Vulnerability, VulnerabilityDetail } from './vulnerabilities.schemas'
export type { AuditLogItem } from './audit-log.schemas'

export function useVulnerabilityComments(id: string) {
  return useQuery({
    queryKey: ['vulnerability', id, 'comments'],
    queryFn: () => fetchVulnerabilityComments({ data: { id } }),
  })
}

export function useVulnerabilityTimeline(id: string) {
  return useQuery({
    queryKey: ['vulnerability', id, 'timeline'],
    queryFn: () => fetchVulnerabilityTimeline({ data: { id } }),
  })
}

export function useAddVulnerabilityComment(id: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (content: string) => addVulnerabilityComment({ data: { id, content } }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vulnerability', id, 'comments'] })
    },
  })
}

export function useGenerateAiReport(id: string) {
  return useMutation({
    mutationFn: (providerName: string) => generateAiReport({ data: { id, providerName } }),
  })
}

export function useUpdateOrganizationalSeverity(id: string) {
  return useMutation({
    mutationFn: (payload: { adjustedSeverity: string; justification: string }) =>
      updateOrganizationalSeverity({ data: { id, ...payload } }),
  })
}
