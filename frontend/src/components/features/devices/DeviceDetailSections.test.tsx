import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { DeviceDetail } from '@/api/devices.schemas'
import { DeviceSection } from '@/components/features/devices/DeviceDetailSections'
import type { MetadataRecord } from '@/components/features/devices/DeviceDetailHelpers'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

// Phase 1 canonical cleanup (Task 15): the previous test suite covered
// SoftwareSection and DeviceActivityTimeline which have been removed
// alongside the legacy AssetDetail shape. Phase 5 will reintroduce the
// vulnerability/software timelines rewired onto the canonical Device
// identity and this suite will grow accordingly.

const deviceFixture: DeviceDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  externalId: 'device-123',
  name: 'WS-CONTOSO-01',
  description: 'Endpoint host',
  criticality: 'High',
  criticalityDetail: {
    source: 'Rule',
    reason: 'Matched the Tier 0 device rule.',
    ruleId: '88888888-8888-8888-8888-888888888888',
    updatedAt: '2026-03-10T10:00:00Z',
  },
  ownerType: 'Team',
  ownerUserName: null,
  ownerUserId: null,
  ownerTeamName: 'Infrastructure Operations',
  ownerTeamId: '33333333-3333-3333-3333-333333333333',
  fallbackTeamName: null,
  fallbackTeamId: null,
  securityProfile: null,
  computerDnsName: 'ws-contoso-01.contoso.local',
  healthStatus: 'Active',
  osPlatform: 'Windows11',
  osVersion: '23H2',
  lastSeenAt: '2026-03-10T12:00:00Z',
  lastIpAddress: '10.0.0.25',
  aadDeviceId: '99999999-9999-9999-9999-999999999999',
  groupId: 'grp-1',
  groupName: 'Tier 0 Servers',
  isAadJoined: true,
  onboardingStatus: 'Onboarded',
  deviceValue: 'Normal',
  risk: null,
  remediation: null,
  businessLabels: [],
  tags: ['tier0', 'pci'],
  metadata: '{}',
}

const metadataFixture: MetadataRecord = {
  source: 'Defender',
  mergedFrom: 2,
}

describe('DeviceSection', () => {
  it('renders normalized host fields, tags, and metadata grid', () => {
    render(<DeviceSection device={deviceFixture} metadata={metadataFixture} />)

    expect(screen.getByText('Host context')).toBeInTheDocument()
    expect(screen.getByText('ws-contoso-01.contoso.local')).toBeInTheDocument()
    expect(screen.getByText('Tier 0 Servers')).toBeInTheDocument()
    expect(screen.getByText('tier0')).toBeInTheDocument()
    expect(screen.getByText('Yes')).toBeInTheDocument()
  })
})
