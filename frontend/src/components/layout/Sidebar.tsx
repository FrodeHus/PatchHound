import { Link, useRouterState } from '@tanstack/react-router'
import { useState, type ComponentType } from 'react'
import {
  BarChart3,
  Bug,
  ClipboardCheck,
  LayoutDashboard,
  ScrollText,
  Server,
  Settings2,
  Shield,
  ShieldAlert,
  ShieldCheck,
  Boxes,
  ChevronDown,
  ChevronRight,
  Laptop,
  Wrench,
} from "lucide-react";
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
  | 'TechnicalManager'
  | 'Stakeholder'

type NavItem = {
  to: string
  label: string
  icon: ComponentType<{ className?: string }>
  roles?: RoleName[]
}

type NavGroup = {
  label: string
  icon: ComponentType<{ className?: string }>
  items: NavItem[]
  roles?: RoleName[]
}

const navItems: NavItem[] = [
  { to: "/dashboard", label: "Overview", icon: LayoutDashboard },
  { to: "/vulnerabilities", label: "Vulnerabilities", icon: Bug },
  {
    to: "/remediation",
    label: "Remediation",
    icon: ShieldAlert,
    roles: ["SecurityManager", "SecurityAnalyst", "GlobalAdmin"],
  },
  {
    to: "/approvals",
    label: "Approvals",
    icon: ClipboardCheck,
    roles: ["GlobalAdmin", "SecurityManager", "TechnicalManager"],
  },
  {
    to: "/audit-log",
    label: "Audit Trail",
    icon: ScrollText,
    roles: ["Auditor", "GlobalAdmin"],
  },
  {
    to: "/settings",
    label: "Settings",
    icon: Settings2,
    roles: ["GlobalAdmin", "SecurityManager"],
  },
  {
    to: "/admin",
    label: "Admin Console",
    icon: ShieldCheck,
    roles: ["GlobalAdmin", "SecurityManager", "SecurityAnalyst", "AssetOwner", "TechnicalManager", "Auditor", "Stakeholder"],
  },
];

const navGroups: NavGroup[] = [
  {
    label: "Dashboards",
    icon: BarChart3,
    items: [
      { to: "/dashboard/executive", label: "Executive Summary", icon: BarChart3, roles: ["Stakeholder", "GlobalAdmin"] },
      { to: "/dashboard/security", label: "Security Summary", icon: ShieldAlert, roles: ["SecurityManager", "GlobalAdmin"] },
      { to: "/dashboard/technical", label: "Technical Summary", icon: Wrench, roles: ["TechnicalManager", "GlobalAdmin"] },
      { to: "/dashboard/my-assets", label: "My Assets", icon: Laptop, roles: ["AssetOwner", "GlobalAdmin"] },
    ],
  },
  {
    label: "Assets",
    icon: Server,
    items: [
      { to: "/devices", label: "Devices", icon: Laptop },
      { to: "/software", label: "Software", icon: Boxes },
    ],
  },
];

function canAccess(item: NavItem, user: CurrentUser): boolean {
  if (!item.roles?.length) {
    return true
  }

  const effective: string[] = ['Stakeholder', ...(user.activeRoles ?? [])]
  return effective.some((role) => item.roles?.includes(role as RoleName))
}

export function Sidebar({
  user,
  onNavigate,
  compact = false,
  collapsed = false,
}: SidebarProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const accessibleItems = navItems.filter((item) => canAccess(item, user));
  const accessibleGroups = navGroups
    .filter((group) => canAccess({ to: '', label: group.label, icon: group.icon, roles: group.roles }, user))
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => canAccess(item, user)),
    }))
    .filter((group) => group.items.length > 0)
  const showLabels = compact || !collapsed;
  const dashboardsGroupActive = pathname.startsWith('/dashboard/')
  const assetsGroupActive =
    pathname.startsWith('/devices') || pathname.startsWith('/software') || pathname.startsWith('/remediation')
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>({
    Dashboards: dashboardsGroupActive || !collapsed,
    Assets: assetsGroupActive || !collapsed,
  })

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
              <div className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
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
        {accessibleGroups.map((group) => {
          const GroupIcon = group.icon
          const isOpen = collapsed && !compact
            ? false
            : ((openGroups[group.label] ?? false)
              || (group.label === 'Dashboards' && dashboardsGroupActive)
              || (group.label === 'Assets' && assetsGroupActive))
          const toggleGroup = () => {
            if (!collapsed || compact) {
              setOpenGroups((prev) => ({ ...prev, [group.label]: !prev[group.label] }))
            }
          }

          return (
            <div key={group.label} className="space-y-1.5">
              <button
                type="button"
                onClick={toggleGroup}
                className={cn(
                  "group flex w-full items-center rounded-2xl border border-transparent text-sm text-sidebar-foreground/84 transition-colors hover:border-sidebar-border/70 hover:bg-sidebar-accent/70 hover:text-sidebar-foreground",
                  collapsed && !compact
                    ? "justify-center px-2 py-3"
                    : "gap-3 px-3.5 py-3",
                )}
                aria-expanded={isOpen}
              >
                <span className="flex size-9 shrink-0 items-center justify-center rounded-xl border border-border/60 bg-background/30 text-muted-foreground transition-colors group-hover:text-primary">
                  <GroupIcon className="size-4" />
                </span>
                {showLabels ? (
                  <>
                    <span className="flex-1 text-left tracking-tight">{group.label}</span>
                    {isOpen ? (
                      <ChevronDown className="size-4 text-muted-foreground" />
                    ) : (
                      <ChevronRight className="size-4 text-muted-foreground" />
                    )}
                  </>
                ) : null}
              </button>

              {isOpen ? (
                <div className="space-y-1 pl-4">
                  {group.items.map((item) => {
                    const Icon = item.icon
                    return (
                      <Link
                        key={item.to}
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        {...({ to: item.to, search: {}, params: {} } as any)}
                        onClick={onNavigate}
                        className="group flex items-center gap-3 rounded-2xl border border-transparent px-3.5 py-2.5 text-sm text-sidebar-foreground/84 transition-colors hover:border-sidebar-border/70 hover:bg-sidebar-accent/70 hover:text-sidebar-foreground"
                        activeProps={{
                          className:
                            "group flex items-center gap-3 rounded-2xl border border-primary/16 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_16%,transparent),transparent_72%),var(--color-card)] px-3.5 py-2.5 text-sm font-medium text-sidebar-foreground shadow-[inset_0_1px_0_rgba(255,255,255,0.04)] [&>span:first-child]:text-primary",
                        }}
                      >
                        <span className="flex size-8 shrink-0 items-center justify-center rounded-xl border border-border/60 bg-background/30 text-muted-foreground transition-colors group-hover:text-primary">
                          <Icon className="size-4" />
                        </span>
                        <span className="tracking-tight">{item.label}</span>
                      </Link>
                    )
                  })}
                </div>
              ) : null}
            </div>
          )
        })}

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
