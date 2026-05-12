import type { ApprovalTaskListItem } from '@/api/approval-tasks.schemas'

export type ApprovalWorkbenchFilters = {
  status: string
  type: string
  search: string
  showRead: boolean
}

type MetricDefinition = {
  label: string
  value: (items: ApprovalTaskListItem[], totalCount: number) => string
}

export type ApprovalTypeOption = {
  value: string
  label: string
}

export type ApprovalWorkbenchConfig = {
  eyebrow: string
  title: string
  description: string
  emptyText: string
  searchPlaceholder: string
  detailRoute: string
  typeOptions: ApprovalTypeOption[]
  metrics: MetricDefinition[]
}

const pendingCount = (items: ApprovalTaskListItem[]) =>
  items.filter((item) => item.status === 'Pending').length

const justificationCount = (items: ApprovalTaskListItem[]) =>
  items.filter((item) =>
    item.type === 'RiskAcceptanceApproval' || item.type === 'PatchingDeferred'
  ).length

const missingMaintenanceWindowCount = (items: ApprovalTaskListItem[]) =>
  items.filter((item) => item.type === 'PatchingApproved' && !item.maintenanceWindowDate).length

export const genericApprovalInboxConfig: ApprovalWorkbenchConfig = {
  eyebrow: 'Approval inbox',
  title: 'Remediation approvals',
  description:
    'Review and action pending remediation approval tasks. Auto-approved and informational items can be marked as read.',
  emptyText: 'No approval tasks match the current filters.',
  searchPlaceholder: 'Search software name',
  detailRoute: '/approvals/$id',
  typeOptions: [
    { value: 'RiskAcceptanceApproval', label: 'Risk exception approval' },
    { value: 'PatchingApproved', label: 'Patch decision review' },
    { value: 'PatchingDeferred', label: 'Deferred patching notice' },
  ],
  metrics: [
    { label: 'Pending approval', value: (items) => String(pendingCount(items)) },
    { label: 'Total tasks', value: (_items, totalCount) => String(totalCount) },
  ],
}

export const securityManagerApprovalWorkbenchConfig: ApprovalWorkbenchConfig = {
  eyebrow: 'Security manager workbench',
  title: 'Security manager approvals',
  description:
    'Approve exception-style remediation decisions where the organization accepts risk, uses alternate mitigation, or defers patching.',
  emptyText: 'No Security Manager approval tasks match the current filters.',
  searchPlaceholder: 'Search software or risk decision',
  detailRoute: '/workbenches/security-manager/cases/$caseId',
  typeOptions: [
    { value: 'RiskAcceptanceApproval', label: 'Risk exception approval' },
    { value: 'PatchingDeferred', label: 'Deferred patching approval' },
  ],
  metrics: [
    { label: 'Pending decisions', value: (items) => String(pendingCount(items)) },
    { label: 'Needs justification', value: (items) => String(justificationCount(items)) },
    { label: 'Total in queue', value: (_items, totalCount) => String(totalCount) },
  ],
}

export const technicalManagerApprovalWorkbenchConfig: ApprovalWorkbenchConfig = {
  eyebrow: 'Technical manager workbench',
  title: 'Technical manager approvals',
  description:
    'Approve patching decisions before execution and make sure maintenance-window details are ready for delivery teams.',
  emptyText: 'No Technical Manager approval tasks match the current filters.',
  searchPlaceholder: 'Search software, vendor, or owner team',
  detailRoute: '/workbenches/technical-manager/cases/$caseId',
  typeOptions: [
    { value: 'PatchingApproved', label: 'Patch decision review' },
  ],
  metrics: [
    { label: 'Pending patch approvals', value: (items) => String(pendingCount(items)) },
    { label: 'Missing maintenance window', value: (items) => String(missingMaintenanceWindowCount(items)) },
    { label: 'Total in queue', value: (_items, totalCount) => String(totalCount) },
  ],
}
