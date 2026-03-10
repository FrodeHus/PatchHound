import { describe, expect, it } from 'vitest'
import { buildSoftwareListRequest, softwareQueryKeys } from '@/features/software/list-state'

describe('buildSoftwareListRequest', () => {
  it('only includes active software filters and paging', () => {
    expect(
      buildSoftwareListRequest({
        search: 'contoso',
        confidence: 'High',
        vulnerableOnly: true,
        boundOnly: false,
        page: 3,
        pageSize: 50,
      }),
    ).toEqual({
      search: 'contoso',
      confidence: 'High',
      vulnerableOnly: true,
      page: 3,
      pageSize: 50,
    })
  })
})

describe('softwareQueryKeys', () => {
  it('builds stable detail-scoped keys', () => {
    expect(
      softwareQueryKeys.list('tenant-1', {
        search: 'contoso',
        confidence: 'High',
        vulnerableOnly: true,
        boundOnly: false,
        page: 1,
        pageSize: 25,
      }),
    ).toEqual([
      'normalized-software',
      'list',
      'tenant-1',
      'contoso',
      'High',
      true,
      false,
      1,
      25,
    ])

    expect(softwareQueryKeys.detail('tenant-1', 'software-1')).toEqual(['normalized-software', 'detail', 'tenant-1', 'software-1'])
    expect(softwareQueryKeys.installations('tenant-1', 'software-1', '2.1.0', 2, 100)).toEqual([
      'normalized-software',
      'installations',
      'tenant-1',
      'software-1',
      '2.1.0',
      2,
      100,
    ])
    expect(softwareQueryKeys.vulnerabilities('tenant-1', 'software-1')).toEqual([
      'normalized-software',
      'vulnerabilities',
      'tenant-1',
      'software-1',
    ])
  })
})
