import { describe, expect, it } from 'vitest'
import { buildVulnerabilitiesListRequest, vulnerabilityQueryKeys } from '@/features/vulnerabilities/list-state'

describe('buildVulnerabilitiesListRequest', () => {
  it('drops empty filters while preserving explicit booleans and paging', () => {
    expect(
      buildVulnerabilitiesListRequest({
        search: '',
        severity: '',
        status: 'Open',
        source: '',
        presentOnly: false,
        recurrenceOnly: true,
        page: 2,
        pageSize: 50,
      }),
    ).toEqual({
      status: 'Open',
      presentOnly: false,
      recurrenceOnly: true,
      page: 2,
      pageSize: 50,
    })
  })
})

describe('vulnerabilityQueryKeys', () => {
  it('builds stable detail-scoped keys for comments and timeline', () => {
    const detail = vulnerabilityQueryKeys.detail('tenant-1', 'vuln-1')

    expect(detail).toEqual(['vulnerabilities', 'detail', 'tenant-1', 'vuln-1'])
    expect(vulnerabilityQueryKeys.comments('tenant-1', 'vuln-1')).toEqual([
      'vulnerabilities',
      'detail',
      'tenant-1',
      'vuln-1',
      'comments',
    ])
    expect(vulnerabilityQueryKeys.timeline('tenant-1', 'vuln-1')).toEqual([
      'vulnerabilities',
      'detail',
      'tenant-1',
      'vuln-1',
      'timeline',
    ])
  })
})
