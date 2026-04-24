import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type {
  PagedTenantSoftwareInstallations,
  TenantSoftwareDetail,
  TenantSoftwareVulnerability,
} from '@/api/software.schemas'
import { SoftwareDetailPage } from '@/components/features/software/SoftwareDetailPage'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

vi.mock('@/components/features/software/SoftwareAiReportTab', () => ({
  SoftwareAiReportTab: () => <div>AI report</div>,
}))

vi.mock('@/components/features/software/SoftwareDescriptionPanel', () => ({
  SoftwareDescriptionPanel: () => <div>Description panel</div>,
}))

vi.mock('@/components/features/software/VersionCohortChooser', () => ({
  VersionCohortChooser: () => <div>Version cohorts</div>,
}))

vi.mock('@/components/features/remediation/SoftwareRemediationView', () => ({
  SoftwareRemediationView: () => <div>Remediation view</div>,
}))

vi.mock('@/components/features/work-notes/WorkNotesSheet', () => ({
  WorkNotesSheet: () => <button type="button">Work notes</button>,
}))

const detailFixture: TenantSoftwareDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  softwareProductId: '22222222-2222-2222-2222-222222222222',
  primarySoftwareAssetId: '33333333-3333-3333-3333-333333333333',
  canonicalName: 'contoso agent',
  canonicalVendor: 'contoso',
  category: 'Application',
  description: 'Desc',
  descriptionGeneratedAt: null,
  descriptionProviderType: null,
  descriptionProfileName: null,
  descriptionModel: null,
  ownerTeamId: '44444444-4444-4444-4444-444444444444',
  ownerTeamName: 'Platform Engineering',
  ownerTeamManagedByRule: true,
  ownerAssignmentSource: 'Rule',
  firstSeenAt: '2026-04-01T00:00:00Z',
  lastSeenAt: '2026-04-10T00:00:00Z',
  activeInstallCount: 3,
  uniqueDeviceCount: 2,
  vulnerableInstallCount: 1,
  activeVulnerabilityCount: 1,
  versionCount: 1,
  exposureImpactScore: 12.5,
  exposureImpactExplanation: null,
  lifecycle: null,
  supplyChainInsight: null,
  versionCohorts: [
    {
      version: '1.0',
      activeInstallCount: 3,
      deviceCount: 2,
      activeVulnerabilityCount: 1,
      firstSeenAt: '2026-04-01T00:00:00Z',
      lastSeenAt: '2026-04-10T00:00:00Z',
    },
  ],
  remediation: {
    openTaskCount: 0,
    overdueTaskCount: 0,
    nearestDueDate: null,
  },
}

const installationsFixture: PagedTenantSoftwareInstallations = {
  items: [],
  page: 1,
  pageSize: 25,
  totalCount: 0,
  totalPages: 1,
}

const vulnerabilitiesFixture: TenantSoftwareVulnerability[] = []

describe('SoftwareDetailPage tabs', () => {
  it('uses shared tab semantics and delegates tab changes', () => {
    const onTabChange = vi.fn()

    render(
      <SoftwareDetailPage
        detail={detailFixture}
        selectedVersion="1.0"
        installations={installationsFixture}
        vulnerabilities={vulnerabilitiesFixture}
        activeTab="overview"
        onTabChange={onTabChange}
        onSelectVersion={() => {}}
        onPageChange={() => {}}
        canViewRemediation
        remediationData={null}
        tenantSoftwareId={detailFixture.id}
        ownerTeams={[]}
        onOwnerTeamChange={() => {}}
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: /AI Insights/i }))

    expect(onTabChange).toHaveBeenCalledWith('ai')
  })

  it('shows the software owner and rule-management state', () => {
    render(
      <SoftwareDetailPage
        detail={detailFixture}
        selectedVersion="1.0"
        installations={installationsFixture}
        vulnerabilities={vulnerabilitiesFixture}
        activeTab="overview"
        onTabChange={() => {}}
        onSelectVersion={() => {}}
        onPageChange={() => {}}
        canViewRemediation
        remediationData={null}
        tenantSoftwareId={detailFixture.id}
        ownerTeams={[]}
        onOwnerTeamChange={() => {}}
      />,
    )

    expect(screen.getAllByText('Platform Engineering')).toHaveLength(2)
    expect(screen.getByText('Rule')).toBeInTheDocument()
  })

  it('shows return-to-rule-control action for manual owner assignments', () => {
    render(
      <SoftwareDetailPage
        detail={{
          ...detailFixture,
          ownerTeamManagedByRule: false,
          ownerAssignmentSource: 'Manual',
        }}
        selectedVersion="1.0"
        installations={installationsFixture}
        vulnerabilities={vulnerabilitiesFixture}
        activeTab="overview"
        onTabChange={() => {}}
        onSelectVersion={() => {}}
        onPageChange={() => {}}
        canViewRemediation
        remediationData={null}
        tenantSoftwareId={detailFixture.id}
        ownerTeams={[]}
        onOwnerTeamChange={() => {}}
      />,
    )

    expect(screen.getByRole('button', { name: /Return to rule control/i })).toBeInTheDocument()
  })
})
