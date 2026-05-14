import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes } from 'react'
import type { DecisionContext } from '@/api/remediation.schemas'
import { AssetOwnerWorkbench } from './AssetOwnerWorkbench'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { to?: string }) => (
    <a href={to} {...props}>{children}</a>
  ),
}))

vi.mock('./DecisionForm', () => ({
  DecisionForm: ({ submitLabel }: { submitLabel: string }) => (
    <form aria-label="owner decision form">
      <button type="button">{submitLabel}</button>
    </form>
  ),
}))

const dataFixture: DecisionContext = {
  remediationCaseId: '11111111-1111-1111-1111-111111111111',
  tenantSoftwareId: '22222222-2222-2222-2222-222222222222',
  softwareName: 'Contoso Agent',
  softwareVendor: 'Contoso',
  softwareCategory: 'Endpoint agent',
  softwareDescription: 'Endpoint security agent installed on managed workstations.',
  softwareOwnerTeamId: '33333333-3333-3333-3333-333333333333',
  softwareOwnerTeamName: 'Platform Engineering',
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
    workflowId: '55555555-5555-5555-5555-555555555555',
    currentStage: 'RemediationDecision',
    currentStageLabel: 'Remediation Decision',
    currentStageDescription: 'Owner team records the remediation decision.',
    currentActorSummary: 'Asset owner',
    canActOnCurrentStage: true,
    currentUserRoles: ['AssetOwner'],
    currentUserTeams: ['Platform Engineering'],
    expectedRoles: ['AssetOwner'],
    expectedTeamName: 'Platform Engineering',
    isInExpectedTeam: true,
    isRecurrence: false,
    hasActiveWorkflow: true,
    stages: [],
  },
  currentDecision: null,
  previousDecision: null,
  latestApprovalResolution: {
    status: 'Rejected',
    justification: 'The proposed date is too far out.',
    resolvedAt: '2026-05-03T00:00:00Z',
    resolvedByDisplayName: 'Sam Security',
  },
  recommendations: [
    {
      id: '77777777-7777-7777-7777-777777777777',
      vulnerabilityId: null,
      recommendedOutcome: 'ApprovedForPatching',
      rationale: 'Patch this endpoint agent because exploitation is likely.',
      priorityOverride: 'Critical',
      analystId: '88888888-8888-8888-8888-888888888888',
      analystDisplayName: 'Casey Analyst',
      createdAt: '2026-05-02T01:00:00Z',
    },
  ],
  topVulnerabilities: [],
  openVulnerabilities: [
    {
      vulnerabilityId: '66666666-6666-6666-6666-666666666666',
      vulnerabilityDefinitionId: '66666666-6666-6666-6666-666666666666',
      externalId: 'CVE-2026-4242',
      title: 'Remote code execution',
      description: 'A remotely exploitable vulnerability.',
      vendorSeverity: 'Critical',
      vendorScore: 9.8,
      effectiveSeverity: 'Critical',
      effectiveScore: 9.8,
      cvssVector: 'CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H',
      firstSeenAt: '2026-05-01T00:00:00Z',
      affectedDeviceCount: 5,
      affectedVersionCount: 1,
      knownExploited: true,
      publicExploit: true,
      activeAlert: false,
      epssScore: 0.42,
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
    jobError: null,
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

describe('AssetOwnerWorkbench', () => {
  it('promotes analyst recommendation and owner decision before vulnerability details', () => {
    render(
      <AssetOwnerWorkbench
        data={dataFixture}
        caseId={dataFixture.remediationCaseId}
        queryKey={['asset-owner-workbench', dataFixture.remediationCaseId]}
      />,
    )

    expect(screen.getByRole('heading', { name: /Contoso Agent/i })).toBeInTheDocument()
    expect(screen.getByText('Patch this endpoint agent because exploitation is likely.')).toBeInTheDocument()
    expect(screen.getByText(/Casey Analyst/i)).toBeInTheDocument()
    expect(screen.getByText(/Critical priority/i)).toBeInTheDocument()
    expect(screen.getByRole('form', { name: /owner decision form/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Submit owner decision/i })).toBeInTheDocument()
  })

  it('keeps technical vulnerability details hidden until requested', () => {
    render(
      <AssetOwnerWorkbench
        data={dataFixture}
        caseId={dataFixture.remediationCaseId}
        queryKey={['asset-owner-workbench', dataFixture.remediationCaseId]}
      />,
    )

    expect(screen.queryByText('CVE-2026-4242')).not.toBeInTheDocument()

    const toggle = screen.getByRole('button', { name: /Show vulnerabilities/i })
    const controlledRegionId = toggle.getAttribute('aria-controls')

    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(controlledRegionId).toBeTruthy()
    expect(document.getElementById(controlledRegionId!)).not.toBeInTheDocument()

    fireEvent.click(toggle)

    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(document.getElementById(controlledRegionId!)).toBeInTheDocument()
    expect(screen.getByText('CVE-2026-4242')).toBeInTheDocument()
    expect(screen.getByText('Remote code execution')).toBeInTheDocument()
  })

  it('shows rejection feedback when a previous approval was returned', () => {
    render(
      <AssetOwnerWorkbench
        data={dataFixture}
        caseId={dataFixture.remediationCaseId}
        queryKey={['asset-owner-workbench', dataFixture.remediationCaseId]}
      />,
    )

    expect(screen.getByText('The proposed date is too far out.')).toBeInTheDocument()
    expect(screen.getByText(/Sam Security/i)).toBeInTheDocument()
  })
})
