export const taskListStatusOptions = ['Open', 'InProgress', 'Completed', 'RiskAccepted'] as const

export const taskUpdateStatusOptions = [
  'Pending',
  'InProgress',
  'PatchScheduled',
  'CannotPatch',
  'Completed',
  'RiskAccepted',
] as const

export function taskStatusRequiresJustification(status: string): boolean {
  return status === 'CannotPatch' || status === 'RiskAccepted'
}
