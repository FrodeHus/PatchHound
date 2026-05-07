import { Link, useRouterState } from '@tanstack/react-router'
import { type ComponentType } from 'react'
import {
  BarChart3,
  Bug,
  ClipboardCheck,
  ClipboardList,
  LayoutDashboard,
  LogOut,
  ScrollText,
  Server,
  Shield,
  ShieldAlert,
  ShieldCheck,
  Boxes,
  Laptop,
  AppWindow,
  Wrench,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover'
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
  expanded?: boolean
  onLogout?: () => void
  onNavigate?: () => void
}

function DockLink({
  item,
  pathname,
  onNavigate,
  expanded,
}: {
  item: NavItem
  pathname: string
  onNavigate?: () => void
  expanded: boolean
}) {
  const Icon = item.icon
  const isActive = pathname === item.to || pathname.startsWith(item.to + '/')

  if (expanded) {
    return (
      <Link
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        {...({ to: item.to, search: {}, params: {} } as any)}
        onClick={onNavigate}
        className={cn('dock-item', isActive && 'active')}
        aria-label={item.label}
      >
        <Icon className="size-5 flex-shrink-0" />
        <span className="dock-label">{item.label}</span>
      </Link>
    )
  }

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

function SectionHeader({ label, expanded }: { label: string; expanded: boolean }) {
  if (!expanded) return <div className="dock-divider my-1" />
  return <div className="dock-section">{label}</div>
}

function DockSubmenu({
  label,
  icon: Icon,
  items,
  active,
  onNavigate,
}: {
  label: string
  icon: ComponentType<{ className?: string }>
  items: NavItem[]
  active: boolean
  onNavigate?: () => void
}) {
  return (
    <Popover>
      <PopoverTrigger render={<button type="button" className={cn('dock-item', active && 'active')} aria-label={label} />}>
        <Icon className="size-5" />
      </PopoverTrigger>
      <PopoverContent side="right" align="center" sideOffset={12} className="dock-submenu">
        <div className="dock-submenu__title">{label}</div>
        <div className="space-y-1">
          {items.map((item) => {
            const ItemIcon = item.icon
            return (
              <Link
                key={item.to}
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                {...({ to: item.to, search: {}, params: {} } as any)}
                onClick={onNavigate}
                className="dock-submenu__link"
              >
                <ItemIcon className="size-4 shrink-0 text-muted-foreground" />
                <span className="min-w-0 truncate">{item.label}</span>
              </Link>
            )
          })}
        </div>
      </PopoverContent>
    </Popover>
  )
}

export function SidebarDock({ user, expanded = false, onLogout, onNavigate }: SidebarDockProps) {
  const pathname = useRouterState({ select: (s) => s.location.pathname })

  const mainItems = allNavItems.filter((item) => canAccess(item, user))
  const visibleDashItems = dashboardItems.filter((item) => canAccess(item, user))
  const visibleAssetItems = assetItems.filter((item) => canAccess(item, user))

  const hasDashboards = visibleDashItems.length > 0
  const hasAssets = visibleAssetItems.length > 0

  return (
    <aside className={cn('dock-sidebar h-dvh', expanded && 'expanded')}>
      {/* Logo */}
      <div className={cn('flex py-4', expanded ? 'items-center gap-3 px-3' : 'items-center justify-center')}>
        {expanded ? (
          <>
            <div className="flex size-8 flex-shrink-0 items-center justify-center rounded-xl border border-primary/20 bg-primary/12 text-primary shadow-[inset_0_1px_0_rgba(255,255,255,0.08)]">
              <Shield className="size-4" />
            </div>
            <span className="text-sm font-semibold text-foreground">PatchHound</span>
          </>
        ) : (
          <Tooltip>
            <TooltipTrigger render={<div />}>
              <div className="flex size-11 items-center justify-center rounded-xl border border-primary/20 bg-primary/12 text-primary shadow-[inset_0_1px_0_rgba(255,255,255,0.08)]">
                <Shield className="size-5" />
              </div>
            </TooltipTrigger>
            <TooltipContent side="right">PatchHound</TooltipContent>
          </Tooltip>
        )}
      </div>

      <div className={cn('dock-divider', expanded && 'mx-3')} />

      {/* Main nav */}
      <nav className={cn(
        'flex flex-1 flex-col gap-1 overflow-y-auto overscroll-contain py-3 [scrollbar-width:none]',
        expanded ? 'px-2' : 'items-center',
      )}>
        {expanded && <div className="dock-section">Operations</div>}

        {mainItems.map((item) => (
          <DockLink key={item.to} item={item} pathname={pathname} onNavigate={onNavigate} expanded={expanded} />
        ))}

        {/* Dashboards group */}
        {hasDashboards && (
          <>
            <SectionHeader label="Dashboards" expanded={expanded} />
            {expanded ? (
              visibleDashItems.map((item) => (
                <DockLink key={item.to} item={item} pathname={pathname} onNavigate={onNavigate} expanded={expanded} />
              ))
            ) : (
              <DockSubmenu
                label="Dashboards"
                icon={BarChart3}
                items={visibleDashItems}
                active={pathname.startsWith('/dashboard/')}
                onNavigate={onNavigate}
              />
            )}
          </>
        )}

        {/* Assets group */}
        {hasAssets && (
          <>
            <SectionHeader label="Assets" expanded={expanded} />
            {expanded ? (
              visibleAssetItems.map((item) => (
                <DockLink key={item.to} item={item} pathname={pathname} onNavigate={onNavigate} expanded={expanded} />
              ))
            ) : (
              <DockSubmenu
                label="Assets"
                icon={Server}
                items={visibleAssetItems}
                active={pathname.startsWith('/devices') || pathname.startsWith('/software') || pathname.startsWith('/assets')}
                onNavigate={onNavigate}
              />
            )}
          </>
        )}
      </nav>

      <div className={cn('dock-divider', expanded && 'mx-3')} />

      {/* Footer */}
      <div className={cn('flex flex-col gap-2 py-3', expanded ? 'px-2' : 'items-center')}>
        {expanded ? (
          <div className="flex items-center gap-2.5 rounded-xl px-3 py-1.5">
            <Avatar className="size-7 flex-shrink-0">
              <AvatarFallback className="bg-primary/15 text-[10px] font-semibold text-primary">
                {getInitials(user.displayName, user.email)}
              </AvatarFallback>
            </Avatar>
            <div className="min-w-0">
              <p className="truncate text-xs font-medium leading-none text-foreground">
                {user.displayName || 'Signed in'}
              </p>
              <p className="mt-0.5 truncate text-[10px] text-muted-foreground">{user.email}</p>
            </div>
          </div>
        ) : (
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
        )}

        {onLogout && (
          expanded ? (
            <button
              type="button"
              className="dock-item text-tone-danger-foreground hover:bg-tone-danger hover:border-tone-danger-border"
              onClick={onLogout}
            >
              <LogOut className="size-4 flex-shrink-0" />
              <span className="dock-label">Logout</span>
            </button>
          ) : (
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
          )
        )}
      </div>
    </aside>
  )
}
