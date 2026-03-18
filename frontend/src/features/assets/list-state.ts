type AssetsListSearch = {
  search: string
  assetType: string
  criticality: string
  ownerType: string
  deviceGroup: string
  healthStatus: string
  riskScore: string
  exposureLevel: string
  tag: string
  unassignedOnly: boolean
  page: number
  pageSize: number
}

export function buildAssetsListRequest(search: AssetsListSearch) {
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.assetType ? { assetType: search.assetType } : {}),
    ...(search.criticality ? { criticality: search.criticality } : {}),
    ...(search.ownerType ? { ownerType: search.ownerType } : {}),
    ...(search.deviceGroup ? { deviceGroup: search.deviceGroup } : {}),
    ...(search.healthStatus ? { healthStatus: search.healthStatus } : {}),
    ...(search.riskScore ? { riskScore: search.riskScore } : {}),
    ...(search.exposureLevel ? { exposureLevel: search.exposureLevel } : {}),
    ...(search.tag ? { tag: search.tag } : {}),
    ...(search.unassignedOnly ? { unassignedOnly: true } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const assetQueryKeys = {
  all: ['assets'] as const,
  list: (tenantId: string | null, search: AssetsListSearch) => [
    ...assetQueryKeys.all,
    'list',
    tenantId,
    search.search,
    search.assetType,
    search.criticality,
    search.ownerType,
    search.deviceGroup,
    search.healthStatus,
    search.riskScore,
    search.exposureLevel,
    search.tag,
    search.unassignedOnly,
    search.page,
    search.pageSize,
  ] as const,
  detail: (tenantId: string | null, assetId: string | null) => [...assetQueryKeys.all, 'detail', tenantId, assetId] as const,
}
