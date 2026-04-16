type VulnerabilitiesListSearch = {
  search: string
  severity: string
  status: string
  source: string
  minAgeDays: string
  publicExploitOnly: boolean
  knownExploitedOnly: boolean
  activeAlertOnly: boolean
  presentOnly: boolean
  page: number
  pageSize: number
}

export function buildVulnerabilitiesListRequest(search: VulnerabilitiesListSearch) {
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.severity ? { severity: search.severity } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.source ? { source: search.source } : {}),
    ...(search.minAgeDays ? { minAgeDays: Number(search.minAgeDays) } : {}),
    ...(search.publicExploitOnly ? { publicExploitOnly: true } : {}),
    ...(search.knownExploitedOnly ? { knownExploitedOnly: true } : {}),
    ...(search.activeAlertOnly ? { activeAlertOnly: true } : {}),
    ...(search.presentOnly ? { presentOnly: true } : {}),
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
    search.minAgeDays,
    search.publicExploitOnly,
    search.knownExploitedOnly,
    search.activeAlertOnly,
    search.presentOnly,
    search.page,
    search.pageSize,
  ] as const,
  detail: (id: string) => [...vulnerabilityQueryKeys.all, 'detail', id] as const,
  comments: (tenantId: string | null, id: string) => [...vulnerabilityQueryKeys.all, 'detail', id, 'tenant', tenantId, 'comments'] as const,
  timeline: (tenantId: string | null, id: string) => [...vulnerabilityQueryKeys.all, 'detail', id, 'tenant', tenantId, 'timeline'] as const,
}
