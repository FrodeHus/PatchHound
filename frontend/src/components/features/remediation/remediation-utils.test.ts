import { describe, expect, it } from 'vitest'
import { formatSoftwareOwnerRoutingDetail } from '@/components/features/remediation/remediation-utils'

describe('formatSoftwareOwnerRoutingDetail', () => {
  it('describes default-team routing when no direct owner is assigned', () => {
    expect(formatSoftwareOwnerRoutingDetail(null, 'Default')).toBe('Default team fallback')
  })
})
