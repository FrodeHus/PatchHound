import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TechnicalManagerDashboardSummary } from '@/api/dashboard.schemas'
import { TechnicalManagerOverview } from '@/components/features/dashboard/TechnicalManagerOverview'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

const summaryFixture: TechnicalManagerDashboardSummary = {
  missedMaintenanceWindowCount: 0,
  approvedPatchingTasks: [
    {
      patchingTaskId: '11111111-1111-1111-1111-111111111111',
      remediationDecisionId: '22222222-2222-2222-2222-222222222222',
      remediationCaseId: '33333333-3333-3333-3333-333333333333',
      softwareName: 'Contoso Agent',
      ownerTeamName: 'Platform Engineering',
      ownerAssignmentSource: 'Rule',
      highestSeverity: 'High',
      affectedDeviceCount: 12,
      approvedAt: '2026-04-24T08:00:00Z',
      dueDate: '2026-04-30T08:00:00Z',
      maintenanceWindowDate: null,
      status: 'Pending',
    },
  ],
  devicesWithAgedVulnerabilities: [],
  approvalTasksRequiringAttention: [],
}

describe('TechnicalManagerOverview', () => {
  it('renders software owner routing for approved patching tasks', () => {
    render(<TechnicalManagerOverview summary={summaryFixture} />)

    expect(screen.getByText(/Rule managed by Platform Engineering/i)).toBeInTheDocument()
    expect(screen.getByText('Rule')).toBeInTheDocument()
  })
})
