import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes } from 'react'
import type { PagedDecisionList } from '@/api/remediation.schemas'
import { MyTasksPage } from './MyTasksPage'
import { bucketsForRoles } from './my-tasks-buckets'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { to?: string }) => (
    <a href={to} {...props}>{children}</a>
  ),
}))

const makeList = (overrides?: Partial<PagedDecisionList>): PagedDecisionList => ({
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
  ...overrides,
})

describe('MyTasksPage', () => {
  it('renders the recommendation section with a link to the security analyst workbench', () => {
    render(
      <MyTasksPage
        sections={[{ bucket: 'recommendation', data: makeList() }]}
        onPageChange={() => {}}
      />,
    )

    expect(screen.getByRole('heading', { name: /My tasks/i })).toBeInTheDocument()
    expect(screen.getByText('Contoso Agent')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Recommendation needed/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Open workbench/i })).toHaveAttribute(
      'href',
      '/workbenches/security-analyst/cases/$caseId',
    )
  })

  it('renders one section per bucket with the right CTA', () => {
    render(
      <MyTasksPage
        sections={[
          { bucket: 'recommendation', data: makeList() },
          { bucket: 'decision', data: makeList({ totalCount: 2 }) },
          { bucket: 'approval', data: makeList({ totalCount: 5 }) },
        ]}
        onPageChange={() => {}}
      />,
    )

    expect(screen.getByRole('heading', { name: /Recommendation needed/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Decision needed/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Approval needed/i })).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Open workbench/i }).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('link', { name: /Open decision workbench/i })[0]).toHaveAttribute(
      'href',
      '/workbenches/asset-owner/cases/$caseId',
    )
    expect(screen.getAllByRole('link', { name: /Review approval/i }).length).toBeGreaterThan(0)
  })

  it('shows an empty-row placeholder per bucket with no items', () => {
    render(
      <MyTasksPage
        sections={[
          { bucket: 'recommendation', data: makeList({ items: [], totalCount: 0 }) },
        ]}
        onPageChange={() => {}}
      />,
    )

    expect(screen.getByText(/Nothing waiting in this queue/i)).toBeInTheDocument()
  })

  it('falls back to a friendly message when the user has no buckets', () => {
    render(<MyTasksPage sections={[]} onPageChange={() => {}} />)
    expect(screen.getByText(/don't have any task queues/i)).toBeInTheDocument()
  })
})

describe('bucketsForRoles', () => {
  it('gives GlobalAdmin every bucket', () => {
    expect(bucketsForRoles(['GlobalAdmin'])).toEqual(['recommendation', 'decision', 'approval'])
  })

  it('maps SecurityAnalyst to recommendation only', () => {
    expect(bucketsForRoles(['SecurityAnalyst'])).toEqual(['recommendation'])
  })

  it('maps AssetOwner to decision only', () => {
    expect(bucketsForRoles(['AssetOwner'])).toEqual(['decision'])
  })

  it('maps SecurityManager to recommendation and approval', () => {
    expect(bucketsForRoles(['SecurityManager'])).toEqual(['recommendation', 'approval'])
  })

  it('combines buckets when the user holds multiple non-admin roles', () => {
    expect(bucketsForRoles(['SecurityAnalyst', 'AssetOwner'])).toEqual(['recommendation', 'decision'])
  })

  it('returns an empty list for unrelated roles', () => {
    expect(bucketsForRoles(['Stakeholder'])).toEqual([])
  })
})
