import { Link } from '@tanstack/react-router'
import type { ComponentType } from 'react'
import { Shield, LayoutDashboard, Bug, CheckSquare, Server, Flag, ScrollText, Users, Settings } from 'lucide-react'
import type { CurrentUser, RoleName } from '@/types/api'

type SidebarProps = {
  user: CurrentUser | null
  isOpen: boolean
  onNavigate: () => void
}

type NavItem = {
  to: string
  label: string
  icon: ComponentType<{ size?: number }>
  roles?: RoleName[]
}

const navItems: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/vulnerabilities', label: 'Vulnerabilities', icon: Bug, roles: ['SecurityManager', 'SecurityAnalyst', 'GlobalAdmin', 'Auditor'] },
  { to: '/tasks', label: 'My Tasks', icon: CheckSquare, roles: ['AssetOwner', 'SecurityManager', 'SecurityAnalyst', 'GlobalAdmin'] },
  { to: '/assets', label: 'Assets', icon: Server, roles: ['SecurityManager', 'SecurityAnalyst', 'GlobalAdmin'] },
  { to: '/campaigns', label: 'Campaigns', icon: Flag, roles: ['SecurityManager', 'SecurityAnalyst', 'GlobalAdmin'] },
  { to: '/audit-log', label: 'Audit Log', icon: ScrollText, roles: ['Auditor', 'GlobalAdmin'] },
  { to: '/admin/users', label: 'Users', icon: Users, roles: ['GlobalAdmin'] },
  { to: '/admin/teams', label: 'Teams', icon: Users, roles: ['GlobalAdmin', 'SecurityManager'] },
  { to: '/settings', label: 'Settings', icon: Settings, roles: ['GlobalAdmin', 'SecurityManager'] },
]

function canAccess(item: NavItem, user: CurrentUser | null): boolean {
  if (!item.roles || item.roles.length === 0) {
    return true
  }

  if (!user) {
    return false
  }

  return user.roles.some((role) => item.roles?.includes(role))
}

export function Sidebar({ user, isOpen, onNavigate }: SidebarProps) {
  return (
    <aside
      className={[
        'fixed inset-y-0 left-0 z-30 w-64 border-r border-border bg-sidebar text-sidebar-foreground transition-transform md:static md:translate-x-0',
        isOpen ? 'translate-x-0' : '-translate-x-full',
      ].join(' ')}
    >
      <div className="flex h-16 items-center gap-2 border-b border-sidebar-border px-4">
        <Shield size={20} className="text-sidebar-primary" />
        <span className="font-semibold">Vigil</span>
      </div>
      <nav className="space-y-1 p-3">
        {navItems.filter((item) => canAccess(item, user)).map((item) => {
          const Icon = item.icon
          return (
            <Link
              key={item.to}
              to={item.to}
              onClick={onNavigate}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
              activeProps={{
                className:
                  'flex items-center gap-2 rounded-md bg-sidebar-primary px-3 py-2 text-sm font-medium text-sidebar-primary-foreground',
              }}
            >
              <Icon size={16} />
              <span>{item.label}</span>
            </Link>
          )
        })}
      </nav>
    </aside>
  )
}
