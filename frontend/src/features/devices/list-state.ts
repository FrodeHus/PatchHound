export type DevicesListSearch = {
  search: string
  criticality: string
  businessLabelId: string
  ownerType: string
  deviceGroups: string
  healthStatus: string
  onboardingStatus: string
  riskBand: string
  tag: string
  unassignedOnly: boolean
  createdWithinHours: number | ''
  lastSeenWithinHours: number | ''
  page: number
  pageSize: number
}

export function buildDevicesListRequest(search: DevicesListSearch) {
  return {
    ...(search.search ? { search: search.search } : {}),
    ...(search.criticality ? { criticality: search.criticality } : {}),
    ...(search.businessLabelId ? { businessLabelId: search.businessLabelId } : {}),
    ...(search.ownerType ? { ownerType: search.ownerType } : {}),
    ...(search.deviceGroups ? { deviceGroups: search.deviceGroups } : {}),
    ...(search.healthStatus ? { healthStatus: search.healthStatus } : {}),
    ...(search.onboardingStatus ? { onboardingStatus: search.onboardingStatus } : {}),
    ...(search.riskBand ? { riskBand: search.riskBand } : {}),
    ...(search.tag ? { tag: search.tag } : {}),
    ...(search.unassignedOnly ? { unassignedOnly: true } : {}),
    ...(typeof search.createdWithinHours === 'number' && search.createdWithinHours > 0
      ? { createdWithinHours: search.createdWithinHours }
      : {}),
    ...(typeof search.lastSeenWithinHours === 'number' && search.lastSeenWithinHours > 0
      ? { lastSeenWithinHours: search.lastSeenWithinHours }
      : {}),
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
    search.deviceGroups,
    search.healthStatus,
    search.onboardingStatus,
    search.riskBand,
    search.tag,
    search.unassignedOnly,
    search.createdWithinHours,
    search.lastSeenWithinHours,
    search.page,
    search.pageSize,
  ] as const,
  detail: (tenantId: string | null, deviceId: string | null) => [...deviceQueryKeys.all, 'detail', tenantId, deviceId] as const,
  remediation: (tenantId: string | null, deviceId: string) => [...deviceQueryKeys.all, 'remediation', tenantId, deviceId] as const,
}
