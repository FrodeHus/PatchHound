type VulnerabilitiesListSearch = {
  search: string
  severity: string
  status: string
  source: string
  presentOnly: boolean
  recurrenceOnly: boolean
  page: number
  pageSize: number
}

export function buildVulnerabilitiesListRequest(search: VulnerabilitiesListSearch) {
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.severity ? { severity: search.severity } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.source ? { source: search.source } : {}),
    ...(search.presentOnly ? { presentOnly: true } : { presentOnly: false }),
    ...(search.recurrenceOnly ? { recurrenceOnly: true } : {}),
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
    search.presentOnly,
    search.recurrenceOnly,
    search.page,
    search.pageSize,
  ] as const,
  detail: (id: string) => [...vulnerabilityQueryKeys.all, 'detail', id] as const,
  comments: (tenantId: string | null, id: string) => [...vulnerabilityQueryKeys.all, 'detail', id, 'tenant', tenantId, 'comments'] as const,
  timeline: (tenantId: string | null, id: string) => [...vulnerabilityQueryKeys.all, 'detail', id, 'tenant', tenantId, 'timeline'] as const,
}
