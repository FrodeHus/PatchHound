import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TopNav } from './TopNav'
import { TenantScopeContext, type TenantScopeContextValue } from '@/components/layout/tenant-scope'
import type { CurrentUser } from '@/server/auth.functions'

const navigateMock = vi.fn()
const invalidateMock = vi.fn()

vi.mock('@tanstack/react-router', () => ({
  useRouter: () => ({
    state: {
      location: {
        pathname: '/',
      },
    },
    navigate: navigateMock,
    invalidate: invalidateMock,
  }),
}))

vi.mock('@/components/layout/Breadcrumbs', () => ({
  Breadcrumbs: () => <div>Breadcrumbs</div>,
}))

vi.mock('@/components/layout/NotificationBell', () => ({
  NotificationBell: () => <div>Notification bell</div>,
}))

vi.mock('@/components/layout/OpenBaoUnsealDialog', () => ({
  OpenBaoUnsealDialog: () => null,
}))

vi.mock('@/components/layout/TenantSelector', () => ({
  TenantSelector: () => <div>Tenant selector</div>,
}))

vi.mock('@/components/layout/ThemeSelector', () => ({
  ThemeSelector: () => <div>Theme selector</div>,
}))

vi.mock('@/server/system.functions', () => ({
  unsealOpenBao: vi.fn(),
}))

const tenantScopeValue: TenantScopeContextValue = {
  selectedTenantId: 'tenant-1',
  tenants: [{ id: 'tenant-1', name: 'Tenant 1' }],
  isLoadingTenants: false,
  setSelectedTenantId: vi.fn(),
}

const globalAdminUser: CurrentUser = {
  id: 'user-1',
  email: 'admin@example.com',
  displayName: 'Global Admin',
  roles: ['GlobalAdmin'],
  activeRoles: ['GlobalAdmin'],
  tenantId: 'tenant-1',
  tenantIds: ['tenant-1'],
  requiresSetup: false,
  systemStatus: {
    openBaoAvailable: true,
    openBaoInitialized: true,
    openBaoSealed: false,
  },
}

describe('TopNav portal view switching', () => {
  it('shows all dashboard view options for global admins', () => {
    window.localStorage.setItem('patchhound:dashboard-view', 'executive')

    render(
      <QueryClientProvider client={new QueryClient()}>
        <TenantScopeContext.Provider value={tenantScopeValue}>
          <TopNav
            user={globalAdminUser}
            onToggleSidebar={() => {}}
            onLogout={() => {}}
          />
        </TenantScopeContext.Provider>
      </QueryClientProvider>,
    )

    fireEvent.click(screen.getByText('Global Admin'))

    expect(screen.getByText('Executive overview')).toBeInTheDocument()
    expect(screen.getByText('Analyst workbench')).toBeInTheDocument()
    expect(screen.getByText('Asset owner view')).toBeInTheDocument()
    expect(screen.getByText('Security manager view')).toBeInTheDocument()
    expect(screen.getByText('Technical manager view')).toBeInTheDocument()
  })
})
