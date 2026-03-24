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

export function outcomeLabel(outcome: string): string {
  switch (outcome) {
    case 'RiskAcceptance': return 'Accept the current risk'
    case 'AlternateMitigation': return 'Use an alternate mitigation'
    case 'ApprovedForPatching': return 'Patch this software'
    case 'PatchingDeferred': return 'Defer patching for now'
    default: return outcome
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
