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
  list: (search: VulnerabilitiesListSearch) => [
    ...vulnerabilityQueryKeys.all,
    'list',
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
  comments: (id: string) => [...vulnerabilityQueryKeys.detail(id), 'comments'] as const,
  timeline: (id: string) => [...vulnerabilityQueryKeys.detail(id), 'timeline'] as const,
}
