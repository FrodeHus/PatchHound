import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes, TextareaHTMLAttributes } from 'react'
import type { DecisionContext } from '@/api/remediation.schemas'
import { SecurityAnalystWorkbench } from './SecurityAnalystWorkbench'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

vi.mock('@/api/remediation.functions', () => ({
  addRecommendation: vi.fn(),
  generateRemediationAiSummary: vi.fn(),
}))

vi.mock('@/components/ui/textarea', () => ({
  Textarea: (props: TextareaHTMLAttributes<HTMLTextAreaElement>) => <textarea {...props} />,
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
  businessLabels: [
    {
      id: '44444444-4444-4444-4444-444444444444',
      name: 'Revenue',
      color: '#22c55e',
      weightCategory: 'Critical',
      riskWeight: 2,
      affectedDeviceCount: 5,
    },
  ],
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
    currentStage: 'SecurityAnalysis',
    currentStageLabel: 'Security Analysis',
    currentStageDescription: 'Security roles review shared exposure and record a recommendation and priority.',
    currentActorSummary: 'Security analyst',
    canActOnCurrentStage: true,
    currentUserRoles: ['SecurityAnalyst'],
    currentUserTeams: [],
    expectedRoles: ['SecurityAnalyst'],
    expectedTeamName: null,
    isInExpectedTeam: null,
    isRecurrence: false,
    hasActiveWorkflow: true,
    stages: [],
  },
  currentDecision: null,
  previousDecision: null,
  latestApprovalResolution: null,
  recommendations: [],
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
  sla: {
    criticalDays: 7,
    highDays: 14,
    mediumDays: 30,
    lowDays: 60,
    slaStatus: 'DueSoon',
    dueDate: '2026-05-09T00:00:00Z',
  },
  aiSummary: {
    content: null,
    ownerRecommendation: null,
    analystAssessment: 'Prioritize patching because exploitation is known.',
    exceptionRecommendation: null,
    recommendedOutcome: 'ApprovedForPatching',
    recommendedPriority: 'Critical',
    status: 'Completed',
    isStale: false,
    reviewStatus: null,
    reviewedAt: null,
    reviewedByDisplayName: null,
    generatedAt: '2026-05-02T00:00:00Z',
    requestedAt: null,
    completedAt: null,
    providerType: null,
    profileName: null,
    model: null,
    canGenerate: true,
    isGenerating: false,
    lastError: null,
    unavailableMessage: null,
  },
}

function renderWorkbench(data: DecisionContext = dataFixture) {
  const queryClient = new QueryClient()
  return render(
    <QueryClientProvider client={queryClient}>
      <SecurityAnalystWorkbench
        data={data}
        caseId={data.remediationCaseId}
        queryKey={['security-analyst-workbench', data.remediationCaseId]}
      />
    </QueryClientProvider>,
  )
}

describe('SecurityAnalystWorkbench', () => {
  it('promotes software context, AI guidance, and recommendation capture', () => {
    renderWorkbench()

    expect(screen.getByRole('heading', { name: /Contoso Agent/i })).toBeInTheDocument()
    expect(screen.getByText('Endpoint security agent installed on managed workstations.')).toBeInTheDocument()
    expect(screen.getByText('Revenue')).toBeInTheDocument()
    expect(screen.getAllByText('Prioritize patching because exploitation is known.').length).toBeGreaterThan(0)
    expect(screen.getByLabelText(/Recommendation rationale/i)).toHaveValue('Prioritize patching because exploitation is known.')
  })

  it('opens vulnerability essentials from the compact list', () => {
    renderWorkbench()

    fireEvent.click(screen.getByRole('button', { name: /Details/i }))

    expect(screen.getByRole('heading', { name: 'CVE-2026-4242' })).toBeInTheDocument()
    expect(screen.getByText('A remotely exploitable vulnerability.')).toBeInTheDocument()
    expect(screen.getByText('CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H')).toBeInTheDocument()
  })

  it('applies the AI draft over an existing saved recommendation', () => {
    renderWorkbench({
      ...dataFixture,
      recommendations: [
        {
          id: '77777777-7777-7777-7777-777777777777',
          vulnerabilityId: null,
          recommendedOutcome: 'RiskAcceptance',
          rationale: 'Existing analyst text.',
          priorityOverride: 'Medium',
          analystId: '88888888-8888-8888-8888-888888888888',
          analystDisplayName: 'Casey Analyst',
          createdAt: '2026-05-02T01:00:00Z',
        },
      ],
    })

    expect(screen.getByLabelText(/Recommendation rationale/i)).toHaveValue('Existing analyst text.')

    fireEvent.click(screen.getByRole('button', { name: /Apply draft/i }))

    expect(screen.getByLabelText(/Recommendation rationale/i)).toHaveValue('Prioritize patching because exploitation is known.')
  })
})
