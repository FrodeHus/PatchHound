export type TaskBucketKey = 'recommendation' | 'decision' | 'approval'

export const BUCKET_FILTERS: Record<TaskBucketKey, Record<string, boolean>> = {
  recommendation: { needsAnalystRecommendation: true },
  decision: { needsRemediationDecision: true },
  approval: { needsApproval: true },
}

export const BUCKET_LABELS: Record<TaskBucketKey, { title: string; description: string; cta: string }> = {
  recommendation: {
    title: 'Recommendation needed',
    description: 'Cases waiting for security analysis before owners can choose a remediation path.',
    cta: 'Open workbench',
  },
  decision: {
    title: 'Decision needed',
    description: 'Cases where the owning team needs to choose a remediation outcome.',
    cta: 'Open case',
  },
  approval: {
    title: 'Approval needed',
    description: 'Decisions waiting for sign-off before remediation can move to execution.',
    cta: 'Review approval',
  },
}

export function bucketsForRoles(roles: readonly string[]): TaskBucketKey[] {
  if (roles.includes('GlobalAdmin')) {
    return ['recommendation', 'decision', 'approval']
  }
  const out: TaskBucketKey[] = []
  if (roles.includes('SecurityAnalyst')) out.push('recommendation')
  if (roles.includes('AssetOwner')) out.push('decision')
  if (roles.includes('SecurityManager')) out.push('approval')
  return out
}
