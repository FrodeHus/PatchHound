import { Link } from '@tanstack/react-router'
import type { ComponentType } from 'react'
import {
  Bug,
  CheckSquare,
  LayoutDashboard,
  ScrollText,
  Server,
  Settings2,
  Shield,
  ShieldCheck,
  Boxes,
} from 'lucide-react'
import { cn } from "@/lib/utils";
import { Separator } from '@/components/ui/separator'
import type { CurrentUser } from '@/server/auth.functions'

type SidebarProps = {
  user: CurrentUser
  onNavigate?: () => void
  compact?: boolean
}

type RoleName =
  | 'SecurityManager'
  | 'SecurityAnalyst'
  | 'GlobalAdmin'
  | 'Auditor'
  | 'AssetOwner'

type NavItem = {
  to: string
  label: string
  icon: ComponentType<{ className?: string }>
  roles?: RoleName[]
}

const navItems: NavItem[] = [
  { to: '/', label: 'Overview', icon: LayoutDashboard },
  { to: '/vulnerabilities', label: 'Vulnerabilities', icon: Bug },
  { to: '/tasks', label: 'Remediation', icon: CheckSquare },
  { to: '/assets', label: 'Assets', icon: Server },
  { to: '/software', label: 'Software', icon: Boxes },
  { to: '/audit-log', label: 'Audit Trail', icon: ScrollText, roles: ['Auditor', 'GlobalAdmin'] },
  { to: '/settings', label: 'Settings', icon: Settings2, roles: ['GlobalAdmin', 'SecurityManager'] },
  { to: '/admin', label: 'Admin Console', icon: ShieldCheck, roles: ['GlobalAdmin', 'SecurityManager'] },
]

function canAccess(item: NavItem, user: CurrentUser): boolean {
  if (!item.roles?.length) {
    return true
  }

  return user.roles.some((role) => item.roles?.includes(role as RoleName))
}

export function Sidebar({ user, onNavigate, compact = false }: SidebarProps) {
  const accessibleItems = navItems.filter((item) => canAccess(item, user))

  return (
    <aside
      className={cn(
        "flex h-full flex-col border-r border-sidebar-border/80 bg-sidebar/70 text-sidebar-foreground backdrop-blur-xl",
        compact ? "w-full" : "w-80",
      )}
    >
      <div className="px-5 pb-5 pt-6">
        <div className="flex items-center gap-3">
          <div className="flex size-11 items-center justify-center rounded-2xl border border-primary/20 bg-primary/12 text-primary shadow-[inset_0_1px_0_rgba(255,255,255,0.08)]">
            <Shield className="size-5" />
          </div>
          <div>
            <div className="text-[11px] uppercase tracking-[0.28em] text-muted-foreground">
              PatchHound Console
            </div>
            <div className="text-lg font-semibold tracking-tight">
              Threat Exposure Control
            </div>
          </div>
        </div>
      </div>

      <div className="px-4">
        <Separator className="bg-sidebar-border/80" />
      </div>

      <nav className="flex-1 space-y-2 px-4 py-5">
        {accessibleItems.map((item) => {
          const Icon = item.icon;
          return (
            <Link
              key={item.to}
              to={item.to}
              onClick={onNavigate}
              className="group flex items-center gap-3 rounded-2xl border border-transparent px-3.5 py-3 text-sm text-sidebar-foreground/84 transition-colors hover:border-sidebar-border/70 hover:bg-sidebar-accent/70 hover:text-sidebar-foreground"
              activeProps={{
                className:
                  "group flex items-center gap-3 rounded-2xl border border-primary/16 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_16%,transparent),transparent_72%),var(--color-card)] px-3.5 py-3 text-sm font-medium text-sidebar-foreground shadow-[inset_0_1px_0_rgba(255,255,255,0.04)] [&>span:first-child]:text-primary",
              }}
            >
              <span className="flex size-9 items-center justify-center rounded-xl border border-border/60 bg-background/30 text-muted-foreground transition-colors group-hover:text-primary">
                <Icon className="size-4" />
              </span>
              <span className="tracking-tight">{item.label}</span>
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
