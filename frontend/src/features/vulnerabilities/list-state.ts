type VulnerabilitiesListSearch = {
  search: string
  severity: string
  status: string
  source: string
  ageOperator: string
  ageHours: string
  publicExploitOnly: boolean
  knownExploitedOnly: boolean
  activeAlertOnly: boolean
  presentOnly: boolean
  remediationCaseIds?: string
  page: number
  pageSize: number
}

export function buildVulnerabilitiesListRequest(search: VulnerabilitiesListSearch) {
  const hasAgeFilter = Boolean(search.ageOperator) && Boolean(search.ageHours)
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.severity ? { severity: search.severity } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.source ? { source: search.source } : {}),
    ...(hasAgeFilter
      ? { ageOperator: search.ageOperator, ageHours: Number(search.ageHours) }
      : {}),
    ...(search.publicExploitOnly ? { publicExploitOnly: true } : {}),
    ...(search.knownExploitedOnly ? { knownExploitedOnly: true } : {}),
    ...(search.activeAlertOnly ? { activeAlertOnly: true } : {}),
    ...(search.presentOnly ? { presentOnly: true } : {}),
    ...(search.remediationCaseIds ? { remediationCaseIds: search.remediationCaseIds } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const vulnerabilityQueryKeys = {
  all: ['vulnerabilities'] as const,
  list: (tenantId: string | null, search: VulnerabilitiesListSearch) => [
    ...vulnerabilityQueryKeys.all,
    'list',
    tenantId,
    search.search,
    search.severity,
    search.status,
    search.source,
    search.ageOperator,
    search.ageHours,
    search.publicExploitOnly,
    search.knownExploitedOnly,
    search.activeAlertOnly,
    search.presentOnly,
    ...(search.remediationCaseIds ? [search.remediationCaseIds] : []),
    search.page,
    search.pageSize,
  ] as const,
  detail: (id: string) => [...vulnerabilityQueryKeys.all, 'detail', id] as const,
  comments: (tenantId: string | null, id: string) => [...vulnerabilityQueryKeys.all, 'detail', id, 'tenant', tenantId, 'comments'] as const,
  timeline: (tenantId: string | null, id: string) => [...vulnerabilityQueryKeys.all, 'detail', id, 'tenant', tenantId, 'timeline'] as const,
}
