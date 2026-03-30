import { describe, expect, it } from 'vitest'
import { assetQueryKeys, buildAssetsListRequest } from '@/features/assets/list-state'

describe('buildAssetsListRequest', () => {
  it('includes only active filters while preserving paging', () => {
    expect(
      buildAssetsListRequest({
        search: 'server',
        assetType: 'Device',
        criticality: '',
        businessLabelId: '',
        ownerType: '',
        deviceGroup: 'Tier 0 Servers',
        healthStatus: '',
        onboardingStatus: '',
        riskScore: '',
        exposureLevel: '',
        tag: '',
        unassignedOnly: true,
        page: 4,
        pageSize: 100,
      }),
    ).toEqual({
      search: 'server',
      assetType: 'Device',
      deviceGroup: 'Tier 0 Servers',
      unassignedOnly: true,
      page: 4,
      pageSize: 100,
    })
  })
})

describe('assetQueryKeys', () => {
  it('builds stable list and detail keys', () => {
    expect(
      assetQueryKeys.list('tenant-1', {
        search: 'server',
        assetType: 'Device',
        criticality: 'Critical',
        businessLabelId: '',
        ownerType: 'Team',
        deviceGroup: 'Tier 0 Servers',
        healthStatus: '',
        onboardingStatus: '',
        riskScore: '',
        exposureLevel: '',
        tag: '',
        unassignedOnly: false,
        page: 2,
        pageSize: 25,
      }),
    ).toEqual([
      'assets',
      'list',
      'tenant-1',
      'server',
      'Device',
      'Critical',
      '',
      'Team',
      'Tier 0 Servers',
      '',
      '',
      '',
      '',
      '',
      false,
      2,
      25,
    ])

    expect(assetQueryKeys.detail('tenant-1', 'asset-1')).toEqual(['assets', 'detail', 'tenant-1', 'asset-1'])
    expect(assetQueryKeys.detail(null, null)).toEqual(['assets', 'detail', null, null])
  })
})
