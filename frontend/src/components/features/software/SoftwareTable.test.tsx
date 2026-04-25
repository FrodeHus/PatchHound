import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TenantSoftwareListItem } from '@/api/software.schemas'
import { SoftwareTable } from '@/components/features/software/SoftwareTable'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
}))

const itemFixture: TenantSoftwareListItem = {
  id: '11111111-1111-1111-1111-111111111111',
  softwareProductId: '22222222-2222-2222-2222-222222222222',
  canonicalName: 'contoso agent',
  canonicalVendor: 'contoso',
  category: 'Application',
  currentRiskScore: 750,
  activeInstallCount: 20,
  uniqueDeviceCount: 15,
  activeVulnerabilityCount: 4,
  versionCount: 3,
  exposureImpactScore: 33,
  lastSeenAt: '2026-04-10T00:00:00Z',
  maintenanceWindowDate: null,
  ownerTeamId: '33333333-3333-3333-3333-333333333333',
  ownerTeamName: 'Platform Engineering',
  ownerTeamManagedByRule: false,
  ownerAssignmentSource: 'Manual',
}

describe('SoftwareTable', () => {
  it('renders the software owner team column', () => {
    render(
      <SoftwareTable
        items={[itemFixture]}
        totalCount={1}
        page={1}
        pageSize={25}
        totalPages={1}
        searchValue=""
        categoryFilter=""
        vulnerableOnly={false}
        missedMaintenanceWindow={false}
        onSearchChange={() => {}}
        onCategoryFilterChange={() => {}}
        onVulnerableOnlyChange={() => {}}
        onMissedMaintenanceWindowChange={() => {}}
        onApplyStructuredFilters={() => {}}
        onShowRiskDetail={() => {}}
        onReturnToRuleControl={() => {}}
        onPageChange={() => {}}
        onPageSizeChange={() => {}}
        onClearFilters={() => {}}
      />,
    )

    expect(screen.getByText('Platform Engineering')).toBeInTheDocument()
    expect(screen.getByText('Manual')).toBeInTheDocument()
  })

  it('offers return to rule control for manual owner assignments', () => {
    const onReturnToRuleControl = vi.fn()

    render(
      <SoftwareTable
        items={[itemFixture]}
        totalCount={1}
        page={1}
        pageSize={25}
        totalPages={1}
        searchValue=""
        categoryFilter=""
        vulnerableOnly={false}
        missedMaintenanceWindow={false}
        onSearchChange={() => {}}
        onCategoryFilterChange={() => {}}
        onVulnerableOnlyChange={() => {}}
        onMissedMaintenanceWindowChange={() => {}}
        onApplyStructuredFilters={() => {}}
        onShowRiskDetail={() => {}}
        onReturnToRuleControl={onReturnToRuleControl}
        onPageChange={() => {}}
        onPageSizeChange={() => {}}
        onClearFilters={() => {}}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /Return to rule control/i }))

    expect(onReturnToRuleControl).toHaveBeenCalledWith(itemFixture.id)
  })
})
