import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { RoleActivationDialog } from '../RoleActivationDialog'
import type { CurrentUser } from '@/server/auth.functions'

vi.mock('@/api/roles.functions', () => ({
  activateRoles: vi.fn(),
}))

vi.mock('@tanstack/react-query', () => ({
  useMutation: vi.fn(() => ({
    mutate: vi.fn(),
    isPending: false,
  })),
  useQueryClient: vi.fn(() => ({
    invalidateQueries: vi.fn(),
  })),
}))

vi.mock('@tanstack/react-router', () => ({
  useRouter: vi.fn(() => ({
    invalidate: vi.fn(),
  })),
}))

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}))

function makeUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'user-1',
    email: 'test@example.com',
    displayName: 'Test User',
    roles: ['Stakeholder', 'SecurityManager', 'Auditor'],
    activeRoles: [],
    tenantId: 'tenant-1',
    tenantIds: ['tenant-1'],
    requiresSetup: false,
    systemStatus: null,
    ...overrides,
  }
}

describe('RoleActivationDialog', () => {
  it('renders Stakeholder as always active and disabled', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser()}
      />,
    )

    expect(screen.getByText('Stakeholder')).toBeInTheDocument()
    expect(screen.getByText('Always active')).toBeInTheDocument()
    expect(screen.getByLabelText('Stakeholder role (always active)')).toHaveAttribute('data-disabled', '')
  })

  it('renders assigned elevated roles with toggles', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser()}
      />,
    )

    expect(screen.getByLabelText('Security Manager role')).toBeInTheDocument()
    expect(screen.getByLabelText('Auditor role')).toBeInTheDocument()
  })

  it('shows empty message when no elevated roles assigned', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser({ roles: ['Stakeholder'] })}
      />,
    )

    expect(
      screen.getByText(/No additional roles are assigned/),
    ).toBeInTheDocument()
  })

  it('reflects active roles as checked', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser({ activeRoles: ['SecurityManager'] })}
      />,
    )

    const smSwitch = screen.getByLabelText('Security Manager role')
    expect(smSwitch).toHaveAttribute('data-checked', '')

    const auditorSwitch = screen.getByLabelText('Auditor role')
    expect(auditorSwitch).not.toHaveAttribute('data-checked')
  })
})
