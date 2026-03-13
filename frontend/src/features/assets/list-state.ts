type AssetsListSearch = {
  search: string
  assetType: string
  criticality: string
  ownerType: string
  deviceGroup: string
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
    search.unassignedOnly,
    search.page,
    search.pageSize,
  ] as const,
  detail: (tenantId: string | null, assetId: string | null) => [...assetQueryKeys.all, 'detail', tenantId, assetId] as const,
}
