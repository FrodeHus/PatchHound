import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AnchorHTMLAttributes } from 'react'
import type { DashboardRiskChangeBrief } from '@/api/dashboard.schemas'
import { RiskChangeWorkbench } from './RiskChangeWorkbench'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { to?: string }) => (
    <a href={to} {...props}>{children}</a>
  ),
}))

const makeBrief = (count: number): DashboardRiskChangeBrief => ({
  appearedCount: count,
  resolvedCount: 0,
  appeared: Array.from({ length: count }, (_, index) => ({
    vulnerabilityId: `${String(index + 1).padStart(8, '0')}-1111-4111-8111-111111111111`,
    externalId: `CVE-2026-${String(index).padStart(4, '0')}`,
    title: `Risk change ${index}`,
    severity: 'High',
    affectedAssetCount: index + 1,
    changedAt: new Date(Date.UTC(2026, 4, 19, 12, 0, 0 - index)).toISOString(),
    remediationCaseId: null,
  })),
  resolved: [],
  aiSummary: null,
})

describe('RiskChangeWorkbench', () => {
  it('loads additional risk changes when the filtered result set is larger than one page', () => {
    render(<RiskChangeWorkbench brief={makeBrief(30)} />)

    expect(screen.getByText('CVE-2026-0000')).toBeInTheDocument()
    expect(screen.queryByText('CVE-2026-0029')).not.toBeInTheDocument()
    expect(screen.getByText('Showing 25 of 30 changes')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Load next 25/i }))

    expect(screen.getByText('CVE-2026-0029')).toBeInTheDocument()
    expect(screen.getByText('Showing 30 of 30 changes')).toBeInTheDocument()
  })
})
