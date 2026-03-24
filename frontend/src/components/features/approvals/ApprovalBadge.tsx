import { toneBadge } from '@/lib/tone-classes'

function typeTone(type: string) {
  switch (type) {
    case 'RiskAcceptanceApproval':
      return 'warning' as const
    case 'PatchingApproved':
      return 'info' as const
    case 'PatchingDeferred':
      return 'info' as const
    default:
      return 'neutral' as const
  }
}

function typeLabel(type: string) {
  switch (type) {
    case 'RiskAcceptanceApproval':
      return 'Risk exception approval'
    case 'PatchingApproved':
      return 'Patch decision review'
    case 'PatchingDeferred':
      return 'Deferred patching notice'
    default:
      return type
  }
}

function statusTone(status: string) {
  switch (status) {
    case 'Pending':
      return 'warning' as const
    case 'Approved':
      return 'success' as const
    case 'Denied':
      return 'danger' as const
    case 'AutoApproved':
      return 'info' as const
    case 'AutoDenied':
      return 'neutral' as const
    default:
      return 'neutral' as const
  }
}

function statusLabel(status: string) {
  switch (status) {
    case 'Pending':
      return 'Pending review'
    case 'Approved':
      return 'Approved'
    case 'Denied':
      return 'Denied'
    case 'AutoApproved':
      return 'Auto-approved'
    case 'AutoDenied':
      return 'Expired'
    default:
      return status
  }
}

export function ApprovalTypeBadge({ type }: { type: string }) {
  return (
    <span
      className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(typeTone(type))}`}
    >
      {typeLabel(type)}
    </span>
  )
}

export function ApprovalStatusBadge({ status }: { status: string }) {
  return (
    <span
      className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(statusTone(status))}`}
    >
      {statusLabel(status)}
    </span>
  )
}
