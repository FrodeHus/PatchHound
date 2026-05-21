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
        ageOperator: '',
        ageHours: '',
        publicExploitOnly: true,
        knownExploitedOnly: false,
        activeAlertOnly: true,
        presentOnly: false,
        hasAssessmentOnly: false,
        page: 2,
        pageSize: 50,
      }),
    ).toEqual({
      status: 'Open',
      publicExploitOnly: true,
      activeAlertOnly: true,
      page: 2,
      pageSize: 50,
    })
  })

  it('includes hasAssessmentOnly when enabled', () => {
    expect(
      buildVulnerabilitiesListRequest({
        search: '',
        severity: '',
        status: '',
        source: '',
        ageOperator: '',
        ageHours: '',
        publicExploitOnly: false,
        knownExploitedOnly: false,
        activeAlertOnly: false,
        presentOnly: false,
        hasAssessmentOnly: true,
        page: 1,
        pageSize: 25,
      }),
    ).toEqual({
      hasAssessmentOnly: true,
      page: 1,
      pageSize: 25,
    })
  })
})

describe('vulnerabilityQueryKeys', () => {
  it('builds stable detail-scoped keys for comments and timeline', () => {
    const detail = vulnerabilityQueryKeys.detail('vuln-1')
    const list = vulnerabilityQueryKeys.list('tenant-1', {
      search: '',
      severity: '',
      status: '',
      source: '',
      ageOperator: '',
      ageHours: '',
      publicExploitOnly: false,
      knownExploitedOnly: false,
      activeAlertOnly: false,
      presentOnly: false,
      hasAssessmentOnly: false,
      page: 1,
      pageSize: 25,
    })

    expect(detail).toEqual(['vulnerabilities', 'detail', 'vuln-1'])
    expect(list).toEqual([
      'vulnerabilities',
      'list',
      'tenant-1',
      '',
      '',
      '',
      '',
      '',
      '',
      false,
      false,
      false,
      false,
      false,
      1,
      25,
    ])
    expect(vulnerabilityQueryKeys.comments('tenant-1', 'vuln-1')).toEqual([
      'vulnerabilities',
      'detail',
      'vuln-1',
      'tenant',
      'tenant-1',
      'comments',
    ])
    expect(vulnerabilityQueryKeys.timeline('tenant-1', 'vuln-1')).toEqual([
      'vulnerabilities',
      'detail',
      'vuln-1',
      'tenant',
      'tenant-1',
      'timeline',
    ])
  })
})
