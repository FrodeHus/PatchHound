import type { AnchorHTMLAttributes, HTMLAttributes, ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SidebarDock } from './SidebarDock'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: AnchorHTMLAttributes<HTMLAnchorElement>) => <a {...props}>{children}</a>,
  useRouterState: () => '/dashboard/security',
}))

vi.mock('@/components/ui/tooltip', () => ({
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  TooltipTrigger: ({ children }: { children: ReactNode }) => <>{children}</>,
  TooltipContent: ({ children, className }: HTMLAttributes<HTMLDivElement>) => (
    <div className={className}>{children}</div>
  ),
}))

vi.mock('@/components/ui/popover', () => ({
  Popover: ({ children }: { children: ReactNode }) => <>{children}</>,
  PopoverTrigger: ({ children }: { children: ReactNode }) => <>{children}</>,
  PopoverContent: ({ children, className }: HTMLAttributes<HTMLDivElement>) => (
    <div className={className}>{children}</div>
  ),
}))

const user = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'analyst@example.test',
  displayName: 'Security Analyst',
  roles: ['GlobalAdmin'],
  activeRoles: ['GlobalAdmin'],
  tenantId: undefined,
  tenantIds: [],
  requiresSetup: false,
  systemStatus: null,
  featureFlags: {},
}

describe('SidebarDock', () => {
  it('uses a readable submenu surface for collapsed dock groups', () => {
    render(<SidebarDock user={user} />)

    expect(screen.getByText('Dashboards').closest('.dock-submenu')).toBeInTheDocument()
    expect(screen.getByText('Security Summary').closest('a')).toHaveClass('dock-submenu__link')
  })
})
