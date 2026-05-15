import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes } from 'react'
import type { MyTaskBucket } from '@/api/my-tasks.schemas'
import { MyTasksPage } from './MyTasksPage'
import { bucketsForRoles } from './my-tasks-buckets'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { to?: string }) => (
    <a href={to} {...props}>{children}</a>
  ),
}))

const makeBucket = (overrides?: Partial<MyTaskBucket>): MyTaskBucket => ({
  bucket: 'recommendation',
  items: [
    {
      remediationCaseId: '11111111-1111-1111-1111-111111111111',
      softwareName: 'Contoso Agent',
      criticality: 'High',
      outcome: null,
      approvalStatus: null,
      totalVulnerabilities: 4,
      criticalCount: 1,
      highCount: 2,
      riskScore: 810,
      riskBand: 'High',
      slaStatus: 'DueSoon',
      slaDueDate: '2026-05-09T00:00:00Z',
      affectedDeviceCount: 12,
      workflowStage: 'SecurityAnalysis',
      softwareOwnerTeamName: 'Platform Engineering',
      softwareOwnerAssignmentSource: 'Rule',
    },
  ],
  page: 1,
  pageSize: 25,
  hasMore: false,
  ...overrides,
})

describe('MyTasksPage', () => {
  it('renders the recommendation section with a link to the security analyst workbench', () => {
    render(
      <MyTasksPage
        sections={[makeBucket()]}
        pageSize={25}
        onLoadNext={() => {}}
        onPageSizeChange={() => {}}
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
          makeBucket(),
          makeBucket({ bucket: 'decision' }),
          makeBucket({ bucket: 'approval' }),
        ]}
        pageSize={25}
        onLoadNext={() => {}}
        onPageSizeChange={() => {}}
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

  it('routes security approval tasks to the security manager case workbench', () => {
    render(
      <MyTasksPage
        sections={[
          makeBucket({
            bucket: 'approval',
            items: [
              {
                ...makeBucket().items[0],
                outcome: 'RiskAcceptance',
                approvalStatus: 'PendingApproval',
              },
            ],
          }),
        ]}
        pageSize={25}
        onLoadNext={() => {}}
        onPageSizeChange={() => {}}
      />,
    )

    expect(screen.getByRole('link', { name: /Review approval/i })).toHaveAttribute(
      'href',
      '/workbenches/security-manager/cases/$caseId',
    )
  })

  it('routes patch approval tasks to the technical manager case workbench', () => {
    render(
      <MyTasksPage
        sections={[
          makeBucket({
            bucket: 'approval',
            items: [
              {
                ...makeBucket().items[0],
                outcome: 'ApprovedForPatching',
                approvalStatus: 'PendingApproval',
              },
            ],
          }),
        ]}
        pageSize={25}
        onLoadNext={() => {}}
        onPageSizeChange={() => {}}
      />,
    )

    expect(screen.getByRole('link', { name: /Review approval/i })).toHaveAttribute(
      'href',
      '/workbenches/technical-manager/cases/$caseId',
    )
  })

  it('shows an empty-row placeholder per bucket with no items', () => {
    render(
      <MyTasksPage
        sections={[
          makeBucket({ items: [] }),
        ]}
        pageSize={25}
        onLoadNext={() => {}}
        onPageSizeChange={() => {}}
      />,
    )

    expect(screen.getByText(/Nothing waiting in this queue/i)).toBeInTheDocument()
  })

  it('falls back to a friendly message when the user has no buckets', () => {
    render(
      <MyTasksPage
        sections={[]}
        pageSize={25}
        onLoadNext={() => {}}
        onPageSizeChange={() => {}}
      />,
    )
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

  it('maps TechnicalManager to approval', () => {
    expect(bucketsForRoles(['TechnicalManager'])).toEqual(['approval'])
  })

  it('combines buckets when the user holds multiple non-admin roles', () => {
    expect(bucketsForRoles(['SecurityAnalyst', 'AssetOwner'])).toEqual(['recommendation', 'decision'])
  })

  it('returns an empty list for unrelated roles', () => {
    expect(bucketsForRoles(['Stakeholder'])).toEqual([])
  })
})
