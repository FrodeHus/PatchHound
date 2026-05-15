import { describe, expect, it } from 'vitest'
import { deviceQueryKeys, buildDevicesListRequest } from '@/features/devices/list-state'

describe('buildDevicesListRequest', () => {
  it('includes only active filters while preserving paging', () => {
    expect(
      buildDevicesListRequest({
        search: 'server',
        criticality: '',
        businessLabelId: '',
        ownerType: '',
        deviceGroup: 'Tier 0 Servers',
        healthStatus: '',
        onboardingStatus: '',
        riskBand: '',
        tag: '',
        unassignedOnly: true,
        page: 4,
        pageSize: 100,
      }),
    ).toEqual({
      search: 'server',
      deviceGroup: 'Tier 0 Servers',
      unassignedOnly: true,
      page: 4,
      pageSize: 100,
    })
  })
})

describe('deviceQueryKeys', () => {
  it('builds stable list and detail keys', () => {
    expect(
      deviceQueryKeys.list('tenant-1', {
        search: 'server',
        criticality: 'Critical',
        businessLabelId: '',
        ownerType: 'Team',
        deviceGroup: 'Tier 0 Servers',
        healthStatus: '',
        onboardingStatus: '',
        riskBand: 'High',
        tag: '',
        unassignedOnly: false,
        page: 2,
        pageSize: 25,
      }),
    ).toEqual([
      'devices',
      'list',
      'tenant-1',
      'server',
      'Critical',
      '',
      'Team',
      'Tier 0 Servers',
      '',
      '',
      'High',
      '',
      false,
      2,
      25,
    ])

    expect(deviceQueryKeys.detail('tenant-1', 'device-1')).toEqual(['devices', 'detail', 'tenant-1', 'device-1'])
    expect(deviceQueryKeys.detail(null, null)).toEqual(['devices', 'detail', null, null])
  })
})
