type SoftwareListSearch = {
  search: string
  confidence: string
  vulnerableOnly: boolean
  boundOnly: boolean
  page: number
  pageSize: number
}

export function buildSoftwareListRequest(search: SoftwareListSearch) {
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.confidence ? { confidence: search.confidence } : {}),
    ...(search.vulnerableOnly ? { vulnerableOnly: true } : {}),
    ...(search.boundOnly ? { boundOnly: true } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const softwareQueryKeys = {
  all: ['normalized-software'] as const,
  list: (search: SoftwareListSearch) => [
    ...softwareQueryKeys.all,
    'list',
    search.search,
    search.confidence,
    search.vulnerableOnly,
    search.boundOnly,
    search.page,
    search.pageSize,
  ] as const,
  detail: (id: string) => [...softwareQueryKeys.all, 'detail', id] as const,
  installations: (id: string, version: string, page: number, pageSize: number) => [
    ...softwareQueryKeys.all,
    'installations',
    id,
    version,
    page,
    pageSize,
  ] as const,
  vulnerabilities: (id: string) => [...softwareQueryKeys.all, 'vulnerabilities', id] as const,
}
