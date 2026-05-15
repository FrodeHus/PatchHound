export type DevicesListSearch = {
  search: string
  criticality: string
  businessLabelId: string
  ownerType: string
  deviceGroup: string
  healthStatus: string
  onboardingStatus: string
  riskBand: string
  tag: string
  unassignedOnly: boolean
  page: number
  pageSize: number
}

export function buildDevicesListRequest(search: DevicesListSearch) {
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.criticality ? { criticality: search.criticality } : {}),
    ...(search.businessLabelId ? { businessLabelId: search.businessLabelId } : {}),
    ...(search.ownerType ? { ownerType: search.ownerType } : {}),
    ...(search.deviceGroup ? { deviceGroup: search.deviceGroup } : {}),
    ...(search.healthStatus ? { healthStatus: search.healthStatus } : {}),
    ...(search.onboardingStatus ? { onboardingStatus: search.onboardingStatus } : {}),
    ...(search.riskBand ? { riskBand: search.riskBand } : {}),
    ...(search.tag ? { tag: search.tag } : {}),
    ...(search.unassignedOnly ? { unassignedOnly: true } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const deviceQueryKeys = {
  all: ['devices'] as const,
  list: (tenantId: string | null, search: DevicesListSearch) => [
    ...deviceQueryKeys.all,
    'list',
    tenantId,
    search.search,
    search.criticality,
    search.businessLabelId,
    search.ownerType,
    search.deviceGroup,
    search.healthStatus,
    search.onboardingStatus,
    search.riskBand,
    search.tag,
    search.unassignedOnly,
    search.page,
    search.pageSize,
  ] as const,
  detail: (tenantId: string | null, deviceId: string | null) => [...deviceQueryKeys.all, 'detail', tenantId, deviceId] as const,
  remediation: (tenantId: string | null, deviceId: string) => [...deviceQueryKeys.all, 'remediation', tenantId, deviceId] as const,
}
