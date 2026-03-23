import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AssetDetail } from '@/api/assets.schemas'
import { DeviceActivityTimeline, SoftwareSection } from '@/components/features/assets/AssetDetailSections'
import type { MetadataRecord } from '@/components/features/assets/AssetDetailHelpers'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

const assetFixture: AssetDetail = {
  id: "11111111-1111-1111-1111-111111111111",
  tenantSoftwareId: "22222222-2222-2222-2222-222222222222",
  externalId: "soft-123",
  name: "Contoso Agent",
  description: "Endpoint agent",
  assetType: "Software",
  criticality: "High",
  criticalityDetail: {
    source: "Rule",
    reason: "Matched the Tier 0 software rule.",
    ruleId: "88888888-8888-8888-8888-888888888888",
    updatedAt: "2026-03-10T10:00:00Z",
  },
  ownerType: "Team",
  ownerUserId: null,
  ownerTeamId: "33333333-3333-3333-3333-333333333333",
  fallbackTeamId: null,
  securityProfile: null,
  deviceComputerDnsName: null,
  deviceHealthStatus: null,
  deviceOsPlatform: null,
  deviceOsVersion: null,
  deviceRiskScore: null,
  deviceLastSeenAt: null,
  deviceLastIpAddress: null,
  deviceAadDeviceId: null,
  deviceGroupId: null,
  deviceGroupName: null,
  deviceExposureLevel: null,
  deviceIsAadJoined: null,
  deviceOnboardingStatus: null,
  deviceValue: null,
  risk: null,
  tags: [],
  softwareCpeBinding: {
    id: "44444444-4444-4444-4444-444444444444",
    cpe23Uri: "cpe:2.3:a:contoso:agent:2.1.0:*:*:*:*:*:*:*",
    bindingMethod: "Manual",
    confidence: "High",
    matchedVendor: "Contoso",
    matchedProduct: "Agent",
    matchedVersion: "2.1.0",
    lastValidatedAt: "2026-03-10T10:00:00Z",
  },
  metadata: "{}",
  vulnerabilities: [
    {
      vulnerabilityId: "55555555-5555-5555-5555-555555555555",
      externalId: "CVE-2026-2000",
      title: "Agent local privesc",
      description: "Escalation issue.",
      vendorSeverity: "High",
      vendorScore: 7.5,
      cvssVector: "CVSS:3.1/AV:L/AC:L/PR:L/UI:N/S:U/C:H/I:H/A:H",
      publishedDate: "2026-03-01T00:00:00Z",
      effectiveSeverity: "High",
      effectiveScore: 7.5,
      assessmentReasonSummary: null,
      status: "Open",
      detectedDate: "2026-03-02T00:00:00Z",
      resolvedDate: null,
      episodeCount: 1,
      episodes: [
        {
          episodeNumber: 1,
          status: "Open",
          firstSeenAt: "2026-03-02T00:00:00Z",
          lastSeenAt: "2026-03-08T00:00:00Z",
          resolvedAt: null,
        },
      ],
      possibleCorrelatedSoftware: [],
    },
  ],
  softwareInventory: [
    {
      softwareAssetId: "66666666-6666-6666-6666-666666666666",
      tenantSoftwareId: null,
      name: "Legacy Tool",
      externalId: "legacy-42",
      lastSeenAt: "2026-03-09T00:00:00Z",
      cpeBinding: null,
      episodeCount: 1,
      episodes: [
        {
          episodeNumber: 1,
          firstSeenAt: "2026-03-03T00:00:00Z",
          lastSeenAt: "2026-03-09T00:00:00Z",
          removedAt: "2026-03-09T00:00:00Z",
        },
      ],
    },
  ],
  knownSoftwareVulnerabilities: [
    {
      vulnerabilityId: "77777777-7777-7777-7777-777777777777",
      externalId: "CVE-2026-3000",
      title: "Remote code execution",
      vendorSeverity: "Critical",
      cvssScore: 9.8,
      cvssVector: "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
      matchMethod: "Defender",
      confidence: "High",
      evidence: "Direct Defender software correlation",
      firstSeenAt: "2026-03-02T00:00:00Z",
      lastSeenAt: "2026-03-08T00:00:00Z",
      resolvedAt: null,
    },
  ],
};

const metadataFixture: MetadataRecord = {
  vendor: 'Contoso',
  version: '2.1.0',
  exposedMachines: 12,
  impactScore: 88,
  weaknesses: 2,
  publicExploit: true,
}

describe('SoftwareSection', () => {
  it('renders software metadata, CPE binding, and known vulnerabilities', () => {
    render(
      <SoftwareSection
        asset={assetFixture}
        metadata={metadataFixture}
        isAssigningSoftwareCpeBinding={false}
        onAssignSoftwareCpeBinding={() => {}}
      />,
    )

    expect(screen.getByText('Inventory intelligence')).toBeInTheDocument()
    expect(screen.getByText('Vendor')).toBeInTheDocument()
    expect(screen.getByText('Matched Vendor')).toBeInTheDocument()
    expect(screen.getByText('Observed')).toBeInTheDocument()
    expect(screen.getByText('cpe:2.3:a:contoso:agent:2.1.0:*:*:*:*:*:*:*')).toBeInTheDocument()
    expect(screen.getByText('Remote code execution')).toBeInTheDocument()
    expect(screen.getByText('Direct Defender software correlation')).toBeInTheDocument()
  })
})

describe('DeviceActivityTimeline', () => {
  it('merges vulnerability and software episode history into timeline entries', () => {
    render(<DeviceActivityTimeline asset={assetFixture} />)

    expect(screen.getByText('Device activity')).toBeInTheDocument()
    expect(screen.getByText('CVE-2026-2000 detected')).toBeInTheDocument()
    expect(screen.getByText('Legacy Tool installed')).toBeInTheDocument()
    expect(screen.getByText('Legacy Tool removed')).toBeInTheDocument()
  })
})
