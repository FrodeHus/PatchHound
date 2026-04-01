type SoftwareListSearch = {
  search: string
  category: string
  vulnerableOnly: boolean
  boundOnly: boolean
  missedMaintenanceWindow: boolean
  page: number
  pageSize: number
}

export function buildSoftwareListRequest(search: SoftwareListSearch) {
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.category ? { category: search.category } : {}),
    ...(search.vulnerableOnly ? { vulnerableOnly: true } : {}),
    ...(search.boundOnly ? { boundOnly: true } : {}),
    ...(search.missedMaintenanceWindow ? { missedMaintenanceWindow: true } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const softwareQueryKeys = {
  all: ['normalized-software'] as const,
  list: (tenantId: string | null, search: SoftwareListSearch) => [
    ...softwareQueryKeys.all,
    'list',
    tenantId,
    search.search,
    search.category,
    search.vulnerableOnly,
    search.boundOnly,
    search.missedMaintenanceWindow,
    search.page,
    search.pageSize,
  ] as const,
  detail: (tenantId: string | null, id: string) => [...softwareQueryKeys.all, 'detail', tenantId, id] as const,
  remediation: (tenantId: string | null, id: string) => [...softwareQueryKeys.all, 'remediation', tenantId, id] as const,
  installations: (tenantId: string | null, id: string, version: string, page: number, pageSize: number) => [
    ...softwareQueryKeys.all,
    'installations',
    tenantId,
    id,
    version,
    page,
    pageSize,
  ] as const,
  vulnerabilities: (tenantId: string | null, id: string) => [...softwareQueryKeys.all, 'vulnerabilities', tenantId, id] as const,
}
