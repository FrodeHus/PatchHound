import { Link, useRouterState } from '@tanstack/react-router'
import { type ComponentType } from 'react'
import {
  BarChart3,
  Bug,
  ClipboardCheck,
  ClipboardList,
  LayoutDashboard,
  ScrollText,
  Shield,
  ShieldAlert,
  ShieldCheck,
  Boxes,
  Laptop,
  AppWindow,
  Wrench,
  LogOut,
  Server,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import type { CurrentUser } from '@/server/auth.functions'

type RoleName =
  | 'CustomerAdmin'
  | 'CustomerOperator'
  | 'CustomerViewer'
  | 'SecurityManager'
  | 'SecurityAnalyst'
  | 'GlobalAdmin'
  | 'Auditor'
  | 'AssetOwner'
  | 'TechnicalManager'
  | 'Stakeholder'

type NavItem = {
  to: string
  label: string
  icon: ComponentType<{ className?: string }>
  roles?: RoleName[]
}

const allNavItems: NavItem[] = [
  { to: '/dashboard', label: 'Overview', icon: LayoutDashboard },
  { to: '/my-tasks', label: 'My Tasks', icon: ClipboardList },
  { to: '/vulnerabilities', label: 'Vulnerabilities', icon: Bug },
  {
    to: '/remediation',
    label: 'Remediation',
    icon: ShieldAlert,
    roles: ['SecurityManager', 'SecurityAnalyst', 'GlobalAdmin'],
  },
  {
    to: '/approvals',
    label: 'Approvals',
    icon: ClipboardCheck,
    roles: ['GlobalAdmin', 'SecurityManager', 'TechnicalManager'],
  },
  {
    to: '/audit-log',
    label: 'Audit Trail',
    icon: ScrollText,
    roles: ['Auditor', 'GlobalAdmin', 'CustomerAdmin'],
  },
  {
    to: '/admin',
    label: 'Admin Console',
    icon: ShieldCheck,
    roles: ['GlobalAdmin', 'CustomerAdmin', 'SecurityManager', 'SecurityAnalyst', 'AssetOwner', 'TechnicalManager', 'Auditor', 'Stakeholder'],
  },
]

const dashboardItems: NavItem[] = [
  { to: '/dashboard/executive', label: 'Executive Summary', icon: BarChart3, roles: ['Stakeholder', 'CustomerViewer', 'GlobalAdmin'] },
  { to: '/dashboard/security', label: 'Security Summary', icon: ShieldAlert, roles: ['SecurityManager', 'CustomerOperator', 'CustomerAdmin', 'GlobalAdmin'] },
  { to: '/dashboard/technical', label: 'Technical Summary', icon: Wrench, roles: ['TechnicalManager', 'CustomerOperator', 'CustomerAdmin', 'GlobalAdmin'] },
  { to: '/dashboard/my-assets', label: 'My Assets', icon: Laptop, roles: ['AssetOwner', 'CustomerOperator', 'CustomerAdmin', 'GlobalAdmin'] },
]

const assetItems: NavItem[] = [
  { to: '/devices', label: 'Devices', icon: Laptop },
  { to: '/software', label: 'Software', icon: Boxes },
  { to: '/assets/applications', label: 'Applications', icon: AppWindow },
]

function canAccess(item: NavItem, user: CurrentUser): boolean {
  if (!item.roles?.length) return true
  const effective: string[] = ['Stakeholder', ...(user.activeRoles ?? [])]
  return effective.some((role) => item.roles?.includes(role as RoleName))
}

function getInitials(displayName: string, email: string) {
  const source = displayName.trim() || email.trim()
  return source
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

type SidebarDockProps = {
  user: CurrentUser
  onLogout?: () => void
  onNavigate?: () => void
}

function DockLink({ item, pathname, onNavigate }: { item: NavItem; pathname: string; onNavigate?: () => void }) {
  const Icon = item.icon
  const isActive = pathname === item.to || pathname.startsWith(item.to + '/')

  return (
    <Tooltip>
      <TooltipTrigger render={<div />}>
        <Link
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          {...({ to: item.to, search: {}, params: {} } as any)}
          onClick={onNavigate}
          className={cn('dock-item', isActive && 'active')}
          aria-label={item.label}
        >
          <Icon className="size-5" />
        </Link>
      </TooltipTrigger>
      <TooltipContent side="right">{item.label}</TooltipContent>
    </Tooltip>
  )
}

export function SidebarDock({ user, onLogout, onNavigate }: SidebarDockProps) {
  const pathname = useRouterState({ select: (s) => s.location.pathname })

  const mainItems = allNavItems.filter((item) => canAccess(item, user))
  const visibleDashItems = dashboardItems.filter((item) => canAccess(item, user))
  const visibleAssetItems = assetItems.filter((item) => canAccess(item, user))

  const hasDashboards = visibleDashItems.length > 0
  const hasAssets = visibleAssetItems.length > 0

  return (
    <aside className="dock-sidebar h-dvh sticky top-0">
      {/* Logo */}
      <div className="flex items-center justify-center py-4">
        <Tooltip>
          <TooltipTrigger render={<div />}>
            <div className="flex size-11 items-center justify-center rounded-xl border border-primary/20 bg-primary/12 text-primary shadow-[inset_0_1px_0_rgba(255,255,255,0.08)]">
              <Shield className="size-5" />
            </div>
          </TooltipTrigger>
          <TooltipContent side="right">PatchHound</TooltipContent>
        </Tooltip>
      </div>

      <div className="dock-divider" />

      {/* Main nav */}
      <nav className="flex flex-1 flex-col items-center gap-1 overflow-y-auto overscroll-contain py-3 [scrollbar-width:none]">
        {mainItems.map((item) => (
          <DockLink key={item.to} item={item} pathname={pathname} onNavigate={onNavigate} />
        ))}

        {/* Dashboards group */}
        {hasDashboards && (
          <>
            <div className="dock-divider my-1" />
            <Tooltip>
              <TooltipTrigger render={<div />}>
                <div className={cn(
                  'dock-item',
                  pathname.startsWith('/dashboard/') && 'active'
                )}>
                  <BarChart3 className="size-5" />
                </div>
              </TooltipTrigger>
              <TooltipContent side="right">
                <div className="text-xs font-medium uppercase tracking-widest text-muted-foreground mb-1">Dashboards</div>
                <div className="space-y-0.5">
                  {visibleDashItems.map((item) => (
                    <Link
                      key={item.to}
                      // eslint-disable-next-line @typescript-eslint/no-explicit-any
                      {...({ to: item.to, search: {}, params: {} } as any)}
                      onClick={onNavigate}
                      className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
                    >
                      {item.label}
                    </Link>
                  ))}
                </div>
              </TooltipContent>
            </Tooltip>
          </>
        )}

        {/* Assets group */}
        {hasAssets && (
          <>
            <div className="dock-divider my-1" />
            <Tooltip>
              <TooltipTrigger render={<div />}>
                <div className={cn(
                  'dock-item',
                  (pathname.startsWith('/devices') || pathname.startsWith('/software') || pathname.startsWith('/assets')) && 'active'
                )}>
                  <Server className="size-5" />
                </div>
              </TooltipTrigger>
              <TooltipContent side="right">
                <div className="text-xs font-medium uppercase tracking-widest text-muted-foreground mb-1">Assets</div>
                <div className="space-y-0.5">
                  {visibleAssetItems.map((item) => (
                    <Link
                      key={item.to}
                      // eslint-disable-next-line @typescript-eslint/no-explicit-any
                      {...({ to: item.to, search: {}, params: {} } as any)}
                      onClick={onNavigate}
                      className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
                    >
                      {item.label}
                    </Link>
                  ))}
                </div>
              </TooltipContent>
            </Tooltip>
          </>
        )}
      </nav>

      <div className="dock-divider" />

      {/* Footer — avatar + logout */}
      <div className="flex flex-col items-center gap-2 py-3">
        <Tooltip>
          <TooltipTrigger render={<div />}>
            <Avatar className="size-9 cursor-default">
              <AvatarFallback className="bg-primary/15 text-[11px] font-semibold text-primary">
                {getInitials(user.displayName, user.email)}
              </AvatarFallback>
            </Avatar>
          </TooltipTrigger>
          <TooltipContent side="right">
            <div className="text-sm font-medium">{user.displayName || 'Signed in'}</div>
            <div className="text-xs text-muted-foreground">{user.email}</div>
          </TooltipContent>
        </Tooltip>

        {onLogout && (
          <Tooltip>
            <TooltipTrigger render={<div />}>
              <button
                type="button"
                className="dock-item text-tone-danger-foreground hover:bg-tone-danger hover:border-tone-danger-border"
                onClick={onLogout}
                aria-label="Logout"
              >
                <LogOut className="size-4" />
              </button>
            </TooltipTrigger>
            <TooltipContent side="right">Logout</TooltipContent>
          </Tooltip>
        )}
      </div>
    </aside>
  )
}
