import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { OwnerDashboardSummary } from '@/api/dashboard.schemas'
import { DeviceOwnerOverview } from '@/components/features/dashboard/DeviceOwnerOverview'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
  useNavigate: () => vi.fn(),
}))

const summaryFixture: OwnerDashboardSummary = {
  ownedAssetCount: 1,
  assetsNeedingAttention: 1,
  openActionCount: 1,
  overdueActionCount: 0,
  topOwnedAssets: [],
  actions: [],
  cloudAppActions: [
    {
      cloudApplicationId: '11111111-1111-1111-1111-111111111111',
      appName: 'Contoso SSO',
      appId: 'app-123',
      ownerTeamName: 'Identity Platform',
      ownerAssignmentSource: 'Rule',
      expiredCredentialCount: 0,
      expiringCredentialCount: 1,
      nearestExpiryAt: '2026-04-30T08:00:00Z',
    },
  ],
}

describe('DeviceOwnerOverview', () => {
  it('renders cloud application owner routing details', () => {
    render(<DeviceOwnerOverview summary={summaryFixture} isLoading={false} />)

    expect(screen.getByText('Rule')).toBeInTheDocument()
    expect(screen.getByText(/Rule managed by Identity Platform/i)).toBeInTheDocument()
  })
})
