import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { PagedDecisionList } from '@/api/remediation.schemas'
import { RemediationWorkbench } from '@/components/features/remediation/RemediationWorkbench'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

vi.mock('@/components/features/remediation/OpenEpisodeSparkline', () => ({
  OpenEpisodeSparkline: () => <div>Sparkline</div>,
}))

const dataFixture: PagedDecisionList = {
  items: [
    {
      remediationCaseId: '11111111-1111-1111-1111-111111111111',
      softwareName: 'Contoso Agent',
      criticality: 'High',
      outcome: null,
      approvalStatus: null,
      decidedAt: null,
      maintenanceWindowDate: null,
      expiryDate: null,
      totalVulnerabilities: 4,
      criticalCount: 1,
      highCount: 2,
      riskScore: 810,
      riskBand: 'High',
      slaStatus: null,
      slaDueDate: null,
      affectedDeviceCount: 12,
      openEpisodeTrend: [],
      workflowStage: 'RemediationDecision',
      softwareOwnerTeamName: 'Platform Engineering',
      softwareOwnerAssignmentSource: 'Rule',
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 25,
  totalPages: 1,
  summary: {
    softwareInScope: 1,
    withDecision: 0,
    pendingApproval: 0,
    noDecision: 1,
  },
}

describe('RemediationWorkbench', () => {
  it('renders software owner routing on remediation rows', () => {
    render(
      <RemediationWorkbench
        data={dataFixture}
        filters={{
          search: '',
          criticality: '',
          outcome: '',
          approvalStatus: '',
          decisionState: '',
          missedMaintenanceWindow: false,
        }}
        onFiltersChange={() => {}}
        onPageChange={() => {}}
      />,
    )

    expect(screen.getByText(/Rule managed by Platform Engineering/i)).toBeInTheDocument()
    expect(screen.getByText('Rule')).toBeInTheDocument()
  })
})
