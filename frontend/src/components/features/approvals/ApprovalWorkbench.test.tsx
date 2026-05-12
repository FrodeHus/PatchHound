import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes } from 'react'
import type { PagedApprovalTaskList } from '@/api/approval-tasks.schemas'
import { ApprovalWorkbench } from './ApprovalWorkbench'
import {
  securityManagerApprovalWorkbenchConfig,
  technicalManagerApprovalWorkbenchConfig,
} from './approval-workbench-config'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { to?: string; params?: { id?: string } }) => (
    <a href={params?.id ? `${to?.replace('$id', params.id)}` : to} {...props}>{children}</a>
  ),
}))

const approvalTasks: PagedApprovalTaskList = {
  items: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      remediationCaseId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      type: 'RiskAcceptanceApproval',
      status: 'Pending',
      softwareName: 'Contoso Agent',
      criticality: 'High',
      outcome: 'RiskAcceptance',
      highestSeverity: 'Critical',
      vulnerabilityCount: 4,
      expiresAt: '2026-05-15T08:00:00Z',
      maintenanceWindowDate: null,
      createdAt: '2026-05-12T08:00:00Z',
      readAt: null,
      decidedByName: 'Alex Owner',
      slaStatus: 'DueSoon',
      slaDueDate: '2026-05-20T08:00:00Z',
    },
    {
      id: '22222222-2222-2222-2222-222222222222',
      remediationCaseId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      type: 'PatchingApproved',
      status: 'Pending',
      softwareName: 'Fabrikam Browser',
      criticality: 'Medium',
      outcome: 'ApprovedForPatching',
      highestSeverity: 'High',
      vulnerabilityCount: 2,
      expiresAt: '2026-05-13T08:00:00Z',
      maintenanceWindowDate: null,
      createdAt: '2026-05-12T09:00:00Z',
      readAt: null,
      decidedByName: 'Riley Owner',
      slaStatus: null,
      slaDueDate: null,
    },
    {
      id: '33333333-3333-3333-3333-333333333333',
      remediationCaseId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
      type: 'PatchingDeferred',
      status: 'Approved',
      softwareName: 'Northwind Runtime',
      criticality: 'Low',
      outcome: 'PatchingDeferred',
      highestSeverity: 'Low',
      vulnerabilityCount: 1,
      expiresAt: '2026-06-01T08:00:00Z',
      maintenanceWindowDate: null,
      createdAt: '2026-05-10T09:00:00Z',
      readAt: null,
      decidedByName: 'Jordan Owner',
      slaStatus: null,
      slaDueDate: null,
    },
  ],
  totalCount: 3,
  page: 1,
  pageSize: 25,
  totalPages: 1,
}

const technicalApprovalTasks: PagedApprovalTaskList = {
  ...approvalTasks,
  items: approvalTasks.items.filter((item) => item.type === 'PatchingApproved'),
  totalCount: 1,
}

describe('ApprovalWorkbench', () => {
  it('renders security manager copy, focused metrics, and security approval links', () => {
    render(
      <ApprovalWorkbench
        config={securityManagerApprovalWorkbenchConfig}
        data={approvalTasks}
        filters={{ status: 'Pending', type: '', search: '', showRead: false }}
        onFiltersChange={() => {}}
        onPageChange={() => {}}
        onMarkRead={() => {}}
      />,
    )

    expect(screen.getByRole('heading', { name: /Security manager approvals/i })).toBeInTheDocument()
    expect(screen.getByText(/Approve exception-style remediation decisions/i)).toBeInTheDocument()
    expect(screen.getByText('Pending decisions')).toBeInTheDocument()
    expect(screen.getByText('Needs justification')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Contoso Agent/i })).toHaveAttribute(
      'href',
      '/workbenches/security-manager/cases/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    )
  })

  it('limits technical manager type filters to patch approvals', () => {
    const onFiltersChange = vi.fn()

    render(
        <ApprovalWorkbench
          config={technicalManagerApprovalWorkbenchConfig}
          data={technicalApprovalTasks}
        filters={{ status: 'Pending', type: 'PatchingApproved', search: '', showRead: false }}
        onFiltersChange={onFiltersChange}
        onPageChange={() => {}}
        onMarkRead={() => {}}
      />,
    )

    expect(screen.getByRole('heading', { name: /Technical manager approvals/i })).toBeInTheDocument()
    expect(screen.getByText(/Approve patching decisions before execution/i)).toBeInTheDocument()
    expect(screen.getByText('Missing maintenance window')).toBeInTheDocument()
    expect(screen.queryByText('Risk exception approval')).not.toBeInTheDocument()
    expect(screen.getByText('Patch decision review')).toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText(/Search software/i), {
      target: { value: 'browser' },
    })

    expect(onFiltersChange).toHaveBeenCalledWith({
      status: 'Pending',
      type: 'PatchingApproved',
      search: 'browser',
      showRead: false,
    })
  })
})
