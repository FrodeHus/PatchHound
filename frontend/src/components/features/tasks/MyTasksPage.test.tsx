import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes } from 'react'
import type { PagedDecisionList } from '@/api/remediation.schemas'
import { MyTasksPage } from './MyTasksPage'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { to?: string }) => (
    <a href={to} {...props}>{children}</a>
  ),
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
      slaStatus: 'DueSoon',
      slaDueDate: '2026-05-09T00:00:00Z',
      affectedDeviceCount: 12,
      openEpisodeTrend: [],
      workflowStage: 'SecurityAnalysis',
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

describe('MyTasksPage', () => {
  it('shows analyst recommendation tasks with a link to the security analyst workbench', () => {
    render(<MyTasksPage data={dataFixture} onPageChange={() => {}} />)

    expect(screen.getByRole('heading', { name: /My tasks/i })).toBeInTheDocument()
    expect(screen.getByText('Contoso Agent')).toBeInTheDocument()
    expect(screen.getAllByText(/Recommendation needed/i).length).toBeGreaterThan(0)
    expect(screen.getByRole('link', { name: /Open workbench/i })).toHaveAttribute(
      'href',
      '/workbenches/security-analyst/cases/$caseId',
    )
  })
})
