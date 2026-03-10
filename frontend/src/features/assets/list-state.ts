type AssetsListSearch = {
  search: string
  assetType: string
  criticality: string
  ownerType: string
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
    ...(search.unassignedOnly ? { unassignedOnly: true } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const assetQueryKeys = {
  all: ['assets'] as const,
  list: (search: AssetsListSearch) => [
    ...assetQueryKeys.all,
    'list',
    search.search,
    search.assetType,
    search.criticality,
    search.ownerType,
    search.unassignedOnly,
    search.page,
    search.pageSize,
  ] as const,
  detail: (assetId: string | null) => [...assetQueryKeys.all, 'detail', assetId] as const,
}
