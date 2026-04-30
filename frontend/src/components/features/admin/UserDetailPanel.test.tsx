import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TenantListItem } from '@/api/settings.schemas'
import type { TeamItem } from '@/api/teams.schemas'
import type { UserDetail } from '@/api/users.schemas'
import { UserDetailPanel } from '@/components/features/admin/UserDetailPanel'
import type { CurrentUser } from '@/server/auth.functions'

vi.mock('@/components/features/audit/AuditTimeline', () => ({
  AuditTimeline: () => <div>Audit timeline</div>,
}))

const currentUser = {
  id: 'current-user',
  email: 'admin@example.com',
  displayName: 'Admin User',
  roles: ['GlobalAdmin'],
  activeRoles: ['GlobalAdmin'],
  tenantId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  tenantIds: ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'],
  requiresSetup: false,
  systemStatus: null,
  featureFlags: {},
} satisfies CurrentUser

const userFixture: UserDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'frode@example.com',
  displayName: 'Frode Hus',
  company: 'Reothor Labs',
  isEnabled: true,
  entraObjectId: '44fbe29b-3b67-4a2a-8f4f-f294468d593c',
  accessScope: 'Internal',
  currentTenantId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  currentTenantName: 'Reothor Labs',
  roles: ['GlobalAdmin'],
  teams: [{ teamId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', teamName: 'SOC', isDefault: false }],
  recentAudit: [],
  tenantAccess: [
    {
      tenantId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      tenantName: 'Reothor Labs',
      roles: ['GlobalAdmin'],
    },
  ],
}

const tenants: TenantListItem[] = [
  {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'Reothor Labs',
    entraTenantId: 'entra-tenant',
    configuredIngestionSourceCount: 1,
    isPrimary: true,
  },
]

const teams: TeamItem[] = [
  {
    id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    tenantId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    tenantName: 'Reothor Labs',
    name: 'SOC',
    isDefault: false,
    isDynamic: false,
    memberCount: 1,
    currentRiskScore: null,
  },
]

function renderPanel(onSaveAccess = vi.fn(), onSaveGroups = vi.fn()) {
  render(
    <UserDetailPanel
      user={userFixture}
      currentUser={currentUser}
      teams={teams}
      tenants={tenants}
      auditItems={[]}
      auditFilters={{ entityType: '', action: '' }}
      isLoading={false}
      isSaving={false}
      onAuditFilterChange={vi.fn()}
      onSaveAccess={onSaveAccess as never}
      onSaveGroups={onSaveGroups as never}
    />,
  )
  return { onSaveAccess, onSaveGroups }
}

describe('UserDetailPanel', () => {
  it('renders a read-first inspector with access-focused tabs', () => {
    renderPanel()

    expect(screen.getAllByText('Frode Hus').length).toBeGreaterThan(0)
    expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /^access$/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /groups/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /audit/i })).toBeInTheDocument()
    expect(screen.getAllByText('1 tenant').length).toBeGreaterThan(0)
    expect(screen.getAllByText('1 assignment group').length).toBeGreaterThan(0)
    expect(screen.getByText('44fbe29b-3b67-4a2a-8f4f-f294468d593c')).toBeInTheDocument()
  })

  it('Save access model calls onSaveAccess with identity and access fields only', () => {
    const onSaveAccess = vi.fn()
    const onSaveGroups = vi.fn()
    renderPanel(onSaveAccess, onSaveGroups)

    fireEvent.click(screen.getByRole('tab', { name: /^access$/i }))
    fireEvent.click(screen.getByRole('button', { name: /save access model/i }))

    expect(onSaveAccess).toHaveBeenCalledOnce()
    expect(onSaveAccess).toHaveBeenCalledWith(
      expect.objectContaining({
        displayName: 'Frode Hus',
        email: 'frode@example.com',
        accessScope: 'Internal',
        isEnabled: true,
        roles: ['GlobalAdmin'],
      }),
    )
    expect(onSaveAccess.mock.calls[0][0]).not.toHaveProperty('teamIds')
    expect(onSaveGroups).not.toHaveBeenCalled()
  })

  it('Save group memberships calls onSaveGroups with teamIds only', () => {
    const onSaveAccess = vi.fn()
    const onSaveGroups = vi.fn()
    renderPanel(onSaveAccess, onSaveGroups)

    fireEvent.click(screen.getByRole('tab', { name: /groups/i }))
    fireEvent.click(screen.getByRole('button', { name: /save group memberships/i }))

    expect(onSaveGroups).toHaveBeenCalledOnce()
    expect(onSaveGroups).toHaveBeenCalledWith({ teamIds: ['bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'] })
    expect(onSaveAccess).not.toHaveBeenCalled()
  })
})
