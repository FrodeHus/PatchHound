import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes } from 'react'
import type { DecisionContext } from '@/api/remediation.schemas'
import { ManagerApprovalCaseWorkbench } from './ManagerApprovalCaseWorkbench'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { to?: string }) => (
    <a href={to} {...props}>{children}</a>
  ),
}))

vi.mock('@/components/ui/textarea', () => ({
  Textarea: (props: React.TextareaHTMLAttributes<HTMLTextAreaElement>) => <textarea {...props} />,
}))

const baseContext: DecisionContext = {
  remediationCaseId: '11111111-1111-1111-1111-111111111111',
  tenantSoftwareId: '22222222-2222-2222-2222-222222222222',
  softwareName: 'Contoso Agent',
  softwareVendor: 'Contoso',
  softwareCategory: 'Endpoint',
  softwareDescription: null,
  softwareOwnerTeamId: '33333333-3333-3333-3333-333333333333',
  softwareOwnerTeamName: 'Platform',
  softwareOwnerAssignmentSource: 'Rule',
  criticality: 'High',
  businessLabels: [],
  summary: {
    totalVulnerabilities: 1,
    criticalCount: 1,
    highCount: 0,
    mediumCount: 0,
    lowCount: 0,
    withKnownExploit: 1,
    withActiveAlert: 0,
  },
  workflow: {
    affectedDeviceCount: 5,
    affectedOwnerTeamCount: 1,
    openPatchingTaskCount: 0,
    completedPatchingTaskCount: 0,
    openEpisodeTrend: [],
  },
  workflowState: {
    workflowId: '44444444-4444-4444-4444-444444444444',
    currentStage: 'Approval',
    currentStageLabel: 'Approval',
    currentStageDescription: 'Waiting for manager approval.',
    currentActorSummary: 'Waiting for manager approval.',
    canActOnCurrentStage: true,
    currentUserRoles: ['SecurityManager'],
    currentUserTeams: [],
    expectedRoles: ['SecurityManager'],
    expectedTeamName: null,
    isInExpectedTeam: null,
    isRecurrence: false,
    hasActiveWorkflow: true,
    stages: [],
  },
  currentDecision: {
    id: '55555555-5555-5555-5555-555555555555',
    outcome: 'RiskAcceptance',
    approvalStatus: 'PendingApproval',
    justification: 'The business accepts this risk until the replacement project lands.',
    decidedBy: '66666666-6666-6666-6666-666666666666',
    decidedAt: '2026-05-12T08:00:00Z',
    approvedBy: null,
    approvedAt: null,
    maintenanceWindowDate: null,
    expiryDate: '2026-06-12T08:00:00Z',
    reEvaluationDate: null,
    latestRejection: null,
    overrides: [],
  },
  previousDecision: null,
  latestApprovalResolution: null,
  recommendations: [
    {
      id: '77777777-7777-7777-7777-777777777777',
      vulnerabilityId: null,
      recommendedOutcome: 'ApprovedForPatching',
      rationale: 'Patch immediately because exploitation is likely.',
      priorityOverride: 'Emergency',
      analystId: '88888888-8888-8888-8888-888888888888',
      analystDisplayName: 'Casey Analyst',
      createdAt: '2026-05-11T08:00:00Z',
    },
  ],
  topVulnerabilities: [],
  openVulnerabilities: [
    {
      vulnerabilityId: '99999999-9999-9999-9999-999999999999',
      vulnerabilityDefinitionId: '99999999-9999-9999-9999-999999999999',
      externalId: 'CVE-2026-4242',
      title: 'Remote code execution',
      description: null,
      vendorSeverity: 'Critical',
      vendorScore: 9.8,
      effectiveSeverity: 'Critical',
      effectiveScore: 9.8,
      cvssVector: null,
      firstSeenAt: '2026-05-01T00:00:00Z',
      affectedDeviceCount: 5,
      affectedVersionCount: 1,
      knownExploited: true,
      publicExploit: true,
      activeAlert: false,
      epssScore: 0.4,
      episodeRiskScore: null,
      overrideOutcome: null,
    },
  ],
  riskScore: null,
  sla: null,
  patchAssessment: {
    vulnerabilityId: null,
    recommendation: null,
    confidence: null,
    summary: null,
    urgencyTier: null,
    urgencyTargetSla: null,
    urgencyReason: null,
    similarVulnerabilities: null,
    compensatingControlsUntilPatched: null,
    references: null,
    aiProfileName: null,
    assessedAt: null,
    jobStatus: 'None',
  },
  threatIntel: {
    summary: null,
    generatedAt: null,
    profileName: null,
    canGenerate: true,
    unavailableMessage: null,
  },
}

describe('ManagerApprovalCaseWorkbench', () => {
  it('renders analyst and owner inputs while hiding vulnerability details by default', () => {
    render(
      <ManagerApprovalCaseWorkbench
        data={baseContext}
        caseId={baseContext.remediationCaseId}
        role="security-manager"
        onResolve={() => {}}
      />,
    )

    expect(screen.getByText('Patch immediately because exploitation is likely.')).toBeInTheDocument()
    expect(screen.getByText(/Emergency priority/i)).toBeInTheDocument()
    expect(screen.getByText(/Casey Analyst/i)).toBeInTheDocument()
    expect(screen.getByText(/The business accepts this risk/i)).toBeInTheDocument()
    expect(screen.queryByText('CVE-2026-4242')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Show vulnerabilities/i }))

    expect(screen.getByText('CVE-2026-4242')).toBeInTheDocument()
  })

  it('requires security manager justification before approval', () => {
    const onResolve = vi.fn()
    render(
      <ManagerApprovalCaseWorkbench
        data={baseContext}
        caseId={baseContext.remediationCaseId}
        role="security-manager"
        onResolve={onResolve}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /^Approve$/i }))

    expect(onResolve).not.toHaveBeenCalled()
    expect(screen.getByText(/Justification is required/i)).toBeInTheDocument()
  })

  it('requires technical manager rejection description and approval deadline', () => {
    const onResolve = vi.fn()
    const technicalContext: DecisionContext = {
      ...baseContext,
      currentDecision: {
        ...baseContext.currentDecision!,
        outcome: 'ApprovedForPatching',
        justification: 'Patch in the next production window.',
        expiryDate: null,
      },
    }

    render(
      <ManagerApprovalCaseWorkbench
        data={technicalContext}
        caseId={technicalContext.remediationCaseId}
        role="technical-manager"
        onResolve={onResolve}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /Return decision/i }))
    expect(onResolve).not.toHaveBeenCalled()
    expect(screen.getByText(/Description is required/i)).toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText(/Record the approval or rejection rationale/i), {
      target: { value: 'Need a deployable patch plan.' },
    })
    fireEvent.click(screen.getByRole('button', { name: /^Approve$/i }))
    expect(onResolve).not.toHaveBeenCalled()
    expect(screen.getByText(/Set a maintenance window/i)).toBeInTheDocument()
  })
})
