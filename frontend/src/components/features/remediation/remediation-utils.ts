import type { Tone } from '@/lib/tone-classes'

export function severityTone(severity: string | null | undefined): Tone {
  switch (severity) {
    case 'Critical': return 'danger'
    case 'High': return 'warning'
    case 'Medium': return 'info'
    default: return 'neutral'
  }
}

export function outcomeTone(outcome: string): Tone {
  switch (outcome) {
    case 'RiskAcceptance': return 'warning'
    case 'AlternateMitigation': return 'info'
    case 'ApprovedForPatching': return 'success'
    case 'PatchingDeferred': return 'danger'
    default: return 'neutral'
  }
}

export function approvalStatusTone(status: string): Tone {
  switch (status) {
    case 'Approved': return 'success'
    case 'PendingApproval': return 'warning'
    case 'Rejected': return 'danger'
    case 'Expired': return 'neutral'
    default: return 'neutral'
  }
}

export function approvalStatusLabel(status: string): string {
  switch (status) {
    case 'PendingApproval': return 'Pending approval'
    case 'Approved': return 'Approved'
    case 'Rejected': return 'Rejected'
    case 'Expired': return 'Expired'
    default: return status
  }
}

export function outcomeLabel(outcome: string): string {
  switch (outcome) {
    case 'RiskAcceptance': return 'Accept the current risk'
    case 'AlternateMitigation': return 'Use an alternate mitigation'
    case 'ApprovedForPatching': return 'Patch this software'
    case 'PatchingDeferred': return 'Defer patching for now'
    default: return outcome
  }
}

export function workflowStageLabel(stage: string): string {
  switch (stage) {
    case 'SecurityAnalysis': return 'Security analysis'
    case 'Verification': return 'Verification'
    case 'RemediationDecision': return 'Awaiting decision'
    case 'Approval': return 'Awaiting approval'
    case 'Execution': return 'In execution'
    case 'Closure': return 'Closing'
    default: return stage
  }
}

export function riskBandTone(band: string): Tone {
  switch (band) {
    case 'Critical': return 'danger'
    case 'High': return 'warning'
    case 'Medium': return 'info'
    case 'Low': return 'success'
    default: return 'neutral'
  }
}

export function formatSoftwareOwnerRoutingDetail(
  ownerTeamName: string | null | undefined,
  assignmentSource: string | null | undefined,
): string {
  switch (assignmentSource) {
    case 'Rule':
      return `Rule managed${ownerTeamName ? ` by ${ownerTeamName}` : ''}`
    case 'Manual':
      return 'Manual override'
    case 'Default':
      return 'Default team fallback'
    case 'Unassigned':
      return 'No owner team assigned'
    default:
      return ownerTeamName ? `Routed to ${ownerTeamName}` : 'Owner routing not available'
  }
}
