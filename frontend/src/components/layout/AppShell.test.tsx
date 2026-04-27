import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AppShell } from '@/components/layout/AppShell'
import type { CurrentUser } from '@/server/auth.functions'

let currentPathname = '/admin/tenants/tenant-1'

vi.mock('@tanstack/react-router', () => ({
  useRouterState: ({ select }: { select: (state: { location: { pathname: string } }) => string }) =>
    select({ location: { pathname: currentPathname } }),
}))

vi.mock('@/components/features/admin/AdminConsoleLayout', () => ({
  AdminConsoleLayout: ({ children }: { children: ReactNode }) => (
    <div data-testid="admin-layout">{children}</div>
  ),
}))

vi.mock('@/components/layout/Sidebar', () => ({
  Sidebar: () => <aside>Sidebar</aside>,
}))

vi.mock('@/components/layout/TenantScopeProvider', () => ({
  TenantScopeProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}))

vi.mock('@/components/layout/TenantUnavailableDialog', () => ({
  TenantUnavailableDialog: () => null,
}))

vi.mock('@/components/layout/TopNav', () => ({
  TopNav: () => <header>Top navigation</header>,
}))

vi.mock('@/components/layout/tenant-scope', () => ({
  useTenantScope: () => ({
    tenantPendingDeletion: false,
    clearTenantPendingDeletion: vi.fn(),
    tenants: [],
    selectedTenantId: 'tenant-1',
    setSelectedTenantId: vi.fn(),
  }),
}))

const userFixture = {
  id: 'user-1',
  email: 'admin@example.com',
  displayName: 'Admin User',
  roles: ['GlobalAdmin'],
  activeRoles: ['GlobalAdmin'],
  tenantId: 'tenant-1',
  tenantIds: ['tenant-1'],
  requiresSetup: false,
  systemStatus: null,
  featureFlags: {},
} satisfies CurrentUser

describe('AppShell content width', () => {
  it('lets admin content start at the left edge and fill available width', () => {
    currentPathname = '/admin/tenants/tenant-1'

    render(
      <AppShell user={userFixture}>
        <main>Admin page</main>
      </AppShell>,
    )

    const adminWrapper = screen.getByTestId('admin-layout').parentElement

    expect(adminWrapper).toHaveClass('w-full')
    expect(adminWrapper).not.toHaveClass('mx-auto')
    expect(adminWrapper).not.toHaveClass('max-w-[1600px]')
  })

  it('keeps non-admin content constrained', () => {
    currentPathname = '/dashboard'

    render(
      <AppShell user={userFixture}>
        <main>Dashboard page</main>
      </AppShell>,
    )

    expect(screen.getByText('Dashboard page').parentElement).toHaveClass(
      'mx-auto',
      'w-full',
      'max-w-[1600px]',
    )
  })
})
