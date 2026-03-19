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
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import type { CurrentUser } from '@/server/auth.functions'

type SidebarProps = {
  user: CurrentUser;
  onNavigate?: () => void;
  compact?: boolean;
  collapsed?: boolean;
};

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

export function Sidebar({
  user,
  onNavigate,
  compact = false,
  collapsed = false,
}: SidebarProps) {
  const accessibleItems = navItems.filter((item) => canAccess(item, user));
  const showLabels = compact || !collapsed;

  return (
    <aside
      className={cn(
        "flex h-full flex-col border-r border-sidebar-border/80 bg-sidebar/70 text-sidebar-foreground backdrop-blur-xl transition-[width] duration-200",
        compact ? "w-full" : collapsed ? "w-[4.5rem]" : "w-80",
      )}
    >
      <div className={cn("pb-5 pt-6", collapsed && !compact ? "px-3" : "px-5")}>
        <div
          className={cn(
            "flex items-center",
            collapsed && !compact ? "justify-center" : "gap-3",
          )}
        >
          <div className="flex size-11 shrink-0 items-center justify-center rounded-2xl border border-primary/20 bg-primary/12 text-primary shadow-[inset_0_1px_0_rgba(255,255,255,0.08)]">
            <Shield className="size-5" />
          </div>
          {showLabels ? (
            <div className="min-w-0">
              <div className="text-[11px] uppercase tracking-[0.28em] text-muted-foreground">
                PatchHound Console
              </div>
              <div className="text-lg font-semibold tracking-tight">
                Threat Exposure Control
              </div>
            </div>
          ) : null}
        </div>
      </div>

      <div className={cn(collapsed && !compact ? "px-2" : "px-4")}>
        <Separator className="bg-sidebar-border/80" />
      </div>

      <nav
        className={cn(
          "flex-1 space-y-2 py-5",
          collapsed && !compact ? "px-2" : "px-4",
        )}
      >
        {accessibleItems.map((item) => {
          const Icon = item.icon;
          const linkContent = (
            <Link
              key={item.to}
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              {...({ to: item.to, search: {}, params: {} } as any)}
              onClick={onNavigate}
              className={cn(
                "group flex items-center rounded-2xl border border-transparent text-sm text-sidebar-foreground/84 transition-colors hover:border-sidebar-border/70 hover:bg-sidebar-accent/70 hover:text-sidebar-foreground",
                collapsed && !compact
                  ? "justify-center px-2 py-3"
                  : "gap-3 px-3.5 py-3",
              )}
              activeProps={{
                className: cn(
                  "group flex items-center rounded-2xl border border-primary/16 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_16%,transparent),transparent_72%),var(--color-card)] text-sm font-medium text-sidebar-foreground shadow-[inset_0_1px_0_rgba(255,255,255,0.04)] [&>span:first-child]:text-primary",
                  collapsed && !compact
                    ? "justify-center px-2 py-3"
                    : "gap-3 px-3.5 py-3",
                ),
              }}
            >
              <span className="flex size-9 shrink-0 items-center justify-center rounded-xl border border-border/60 bg-background/30 text-muted-foreground transition-colors group-hover:text-primary">
                <Icon className="size-4" />
              </span>
              {showLabels ? (
                <span className="tracking-tight">{item.label}</span>
              ) : null}
            </Link>
          );

          if (collapsed && !compact) {
            return (
              <Tooltip key={item.to}>
                <TooltipTrigger render={<div />}>{linkContent}</TooltipTrigger>
                <TooltipContent side="right">{item.label}</TooltipContent>
              </Tooltip>
            );
          }

          return linkContent;
        })}
      </nav>
    </aside>
  );
}
