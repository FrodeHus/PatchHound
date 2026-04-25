import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { DeviceDetail, DeviceExposure } from '@/api/devices.schemas'
import { DeviceDetailPageView } from '@/components/features/devices/DeviceDetailPageView'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

vi.mock('@/components/features/work-notes/WorkNotesSheet', () => ({
  WorkNotesSheet: () => <button type="button">Work notes</button>,
}))

vi.mock('@/components/features/devices/DeviceAdvancedToolsPanel', () => ({
  DeviceAdvancedToolsPanel: () => <div>Advanced tools</div>,
}))

const deviceFixture: DeviceDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  externalId: 'device-123',
  name: 'WS-CONTOSO-01',
  description: 'Endpoint host',
  criticality: 'High',
  criticalityDetail: null,
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
  riskScore: 'High',
  lastSeenAt: '2026-03-10T12:00:00Z',
  lastIpAddress: '10.0.0.25',
  aadDeviceId: '99999999-9999-9999-9999-999999999999',
  groupId: 'grp-1',
  groupName: 'Tier 0 Servers',
  exposureLevel: 'Medium',
  isAadJoined: true,
  onboardingStatus: 'Onboarded',
  deviceValue: 'Normal',
  risk: null,
  remediation: null,
  businessLabels: [],
  tags: ['tier0', 'pci'],
  metadata: '{}',
}

const exposureFixture: DeviceExposure = {
  exposureId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  vulnerabilityId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  externalId: 'CVE-2026-1',
  title: 'Test exposure',
  severity: 'High',
  matchedVersion: '1.0',
  matchSource: 'Product',
  status: 'Open',
  environmentalCvss: 8.2,
  firstObservedAt: '2026-03-10T12:00:00Z',
  lastObservedAt: '2026-03-11T12:00:00Z',
  resolvedAt: null,
}

describe('Device exposures section', () => {
  it('renders exposures list with environmental cvss column', () => {
    render(
      <DeviceDetailPageView
        device={deviceFixture}
        exposures={[exposureFixture]}
        canUseAdvancedTools={false}
        securityProfiles={[]}
        availableBusinessLabels={[]}
        isAssigningSecurityProfile={false}
        isSettingCriticality={false}
        isResettingCriticality={false}
        isAssigningBusinessLabels={false}
        onAssignSecurityProfile={() => {}}
        onSetCriticality={() => {}}
        onResetCriticality={() => {}}
        onAssignBusinessLabels={() => {}}
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: 'Exposures' }))
    expect(screen.getByText('CVE-2026-1')).toBeInTheDocument()
    expect(screen.getByText('Environmental CVSS')).toBeInTheDocument()
    expect(screen.getByText('8.2')).toBeInTheDocument()
  })

  it('renders empty state when no exposures exist', () => {
    render(
      <DeviceDetailPageView
        device={deviceFixture}
        exposures={[]}
        canUseAdvancedTools={false}
        securityProfiles={[]}
        availableBusinessLabels={[]}
        isAssigningSecurityProfile={false}
        isSettingCriticality={false}
        isResettingCriticality={false}
        isAssigningBusinessLabels={false}
        onAssignSecurityProfile={() => {}}
        onSetCriticality={() => {}}
        onResetCriticality={() => {}}
        onAssignBusinessLabels={() => {}}
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: 'Exposures' }))
    expect(screen.getByText('No exposures observed for this device.')).toBeInTheDocument()
  })
})
