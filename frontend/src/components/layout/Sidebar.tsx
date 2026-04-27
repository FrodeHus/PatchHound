import { Link, useRouterState } from '@tanstack/react-router'
import { useState, type ComponentType } from 'react'
import {
  BarChart3,
  Bug,
  ClipboardCheck,
  LayoutDashboard,
  ScrollText,
  Server,
  Shield,
  ShieldAlert,
  ShieldCheck,
  Boxes,
  ChevronDown,
  ChevronRight,
  Laptop,
  AppWindow,
  Wrench,
  LogOut,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Separator } from '@/components/ui/separator'
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import type { CurrentUser } from '@/server/auth.functions'

type SidebarProps = {
  user: CurrentUser;
  onNavigate?: () => void;
  onLogout?: () => void;
  compact?: boolean;
  collapsed?: boolean;
};

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
    roles: ["Auditor", "GlobalAdmin", "CustomerAdmin"],
  },
  {
    to: "/admin",
    label: "Admin Console",
    icon: ShieldCheck,
    roles: ["GlobalAdmin", "CustomerAdmin", "SecurityManager", "SecurityAnalyst", "AssetOwner", "TechnicalManager", "Auditor", "Stakeholder"],
  },
];

const navGroups: NavGroup[] = [
  {
    label: "Dashboards",
    icon: BarChart3,
    items: [
      { to: "/dashboard/executive", label: "Executive Summary", icon: BarChart3, roles: ["Stakeholder", "CustomerViewer", "GlobalAdmin"] },
      { to: "/dashboard/security", label: "Security Summary", icon: ShieldAlert, roles: ["SecurityManager", "CustomerOperator", "CustomerAdmin", "GlobalAdmin"] },
      { to: "/dashboard/technical", label: "Technical Summary", icon: Wrench, roles: ["TechnicalManager", "CustomerOperator", "CustomerAdmin", "GlobalAdmin"] },
      { to: "/dashboard/my-assets", label: "My Assets", icon: Laptop, roles: ["AssetOwner", "CustomerOperator", "CustomerAdmin", "GlobalAdmin"] },
    ],
  },
  {
    label: "Assets",
    icon: Server,
    items: [
      { to: "/devices", label: "Devices", icon: Laptop },
      { to: "/software", label: "Software", icon: Boxes },
      { to: "/assets/applications", label: "Applications", icon: AppWindow },
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

function getInitials(displayName: string, email: string) {
  const source = displayName.trim() || email.trim();
  return source
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("");
}

export function Sidebar({
  user,
  onNavigate,
  onLogout,
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
    pathname.startsWith('/devices') || pathname.startsWith('/software') || pathname.startsWith('/remediation') || pathname.startsWith('/assets')
  const [openGroups, setOpenGroups] = useState<Record<string, boolean>>({
    Dashboards: dashboardsGroupActive || !collapsed,
    Assets: assetsGroupActive || !collapsed,
  })
  const visibleRoles = user.activeRoles?.length
    ? `${user.activeRoles.length} role${user.activeRoles.length === 1 ? "" : "s"} active`
    : "Stakeholder"
  const navPadding = collapsed && !compact ? "px-2" : "px-4"
  const linkClassName = cn(
    "group flex min-h-11 items-center rounded-xl border border-transparent text-sm text-sidebar-foreground/84 transition-colors hover:border-sidebar-border/70 hover:bg-sidebar-accent/80 hover:text-sidebar-foreground",
    collapsed && !compact
      ? "justify-center px-2 py-2.5"
      : "gap-3 px-3 py-2.5",
  )
  const activeLinkClassName = cn(
    "group flex min-h-11 items-center rounded-xl border border-primary/20 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_16%,transparent),transparent_76%),color-mix(in_oklab,var(--sidebar-accent)_86%,var(--card))] text-sm font-medium text-sidebar-foreground shadow-[inset_0_1px_0_rgba(255,255,255,0.05)] [&>span:first-child]:border-primary/24 [&>span:first-child]:bg-primary/14 [&>span:first-child]:text-primary",
    collapsed && !compact
      ? "justify-center px-2 py-2.5"
      : "gap-3 px-3 py-2.5",
  )

  const renderNavLink = (item: NavItem, iconSize = "size-9") => {
    const Icon = item.icon
    const linkContent = (
      <Link
        key={item.to}
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        {...({ to: item.to, search: {}, params: {} } as any)}
        onClick={onNavigate}
        className={linkClassName}
        activeProps={{ className: activeLinkClassName }}
      >
        <span
          className={cn(
            "flex shrink-0 items-center justify-center rounded-lg border border-sidebar-border/60 bg-background/20 text-muted-foreground transition-colors group-hover:text-primary",
            iconSize,
          )}
        >
          <Icon className="size-4" />
        </span>
        {showLabels ? (
          <span className="min-w-0 truncate tracking-tight">{item.label}</span>
        ) : null}
      </Link>
    )

    if (collapsed && !compact) {
      return (
        <Tooltip key={item.to}>
          <TooltipTrigger render={<div />}>{linkContent}</TooltipTrigger>
          <TooltipContent side="right">{item.label}</TooltipContent>
        </Tooltip>
      )
    }

    return linkContent
  }

  return (
    <aside
      className={cn(
        "flex h-full min-h-0 flex-col overflow-hidden border-r border-sidebar-border/80 bg-sidebar/88 text-sidebar-foreground shadow-[inset_-1px_0_0_color-mix(in_oklab,var(--sidebar-border)_72%,transparent)] backdrop-blur-xl transition-[width] duration-200",
        compact ? "w-full" : collapsed ? "w-[4.5rem]" : "w-80",
      )}
    >
      <div className={cn("shrink-0 pb-4 pt-5", collapsed && !compact ? "px-3" : "px-5")}>
        <div
          className={cn(
            "flex items-center",
            collapsed && !compact ? "justify-center" : "gap-3",
          )}
        >
          <div className="flex size-11 shrink-0 items-center justify-center rounded-xl border border-primary/20 bg-primary/12 text-primary shadow-[inset_0_1px_0_rgba(255,255,255,0.08)]">
            <Shield className="size-5" />
          </div>
          {showLabels ? (
            <div className="min-w-0">
              <div className="text-[10px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
                PatchHound Console
              </div>
              <div className="truncate text-base font-semibold tracking-tight">
                Threat Exposure Control
              </div>
            </div>
          ) : null}
        </div>
      </div>

      <div className={cn("shrink-0", navPadding)}>
        <Separator className="bg-sidebar-border/80" />
      </div>

      <nav
        className={cn(
          "min-h-0 flex-1 space-y-5 overflow-y-auto overscroll-contain py-4 [scrollbar-width:thin] [scrollbar-color:color-mix(in_oklab,var(--muted-foreground)_36%,transparent)_transparent]",
          navPadding,
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
            <section key={group.label} className="space-y-1.5">
              {showLabels ? (
                <div className="px-2 text-[10px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
                  {group.label}
                </div>
              ) : null}

              {showLabels ? (
                <button
                  type="button"
                  onClick={toggleGroup}
                  className="group flex min-h-11 w-full items-center gap-3 rounded-xl border border-transparent px-3 py-2.5 text-sm text-sidebar-foreground/84 transition-colors hover:border-sidebar-border/70 hover:bg-sidebar-accent/80 hover:text-sidebar-foreground"
                  aria-expanded={isOpen}
                >
                  <span className="flex size-9 shrink-0 items-center justify-center rounded-lg border border-sidebar-border/60 bg-background/20 text-muted-foreground transition-colors group-hover:text-primary">
                    <GroupIcon className="size-4" />
                  </span>
                  <span className="min-w-0 flex-1 truncate text-left tracking-tight">
                    {group.label === "Dashboards" ? "Dashboard views" : "Asset inventory"}
                  </span>
                  {isOpen ? (
                    <ChevronDown className="size-4 text-muted-foreground" />
                  ) : (
                    <ChevronRight className="size-4 text-muted-foreground" />
                  )}
                </button>
              ) : null}

              {isOpen || !showLabels ? (
                <div className={cn("space-y-1", showLabels ? "pl-3" : "")}>
                  {group.items.map((item) => renderNavLink(item, showLabels ? "size-8" : "size-9"))}
                </div>
              ) : null}
            </section>
          )
        })}

        <section className="space-y-1.5">
          {showLabels ? (
            <div className="px-2 text-[10px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
              Operations
            </div>
          ) : null}
          <div className="space-y-1">
            {accessibleItems.map((item) => renderNavLink(item))}
          </div>
        </section>
      </nav>

      <div className={cn("shrink-0 space-y-3 border-t border-sidebar-border/80 py-3", navPadding)}>
        {showLabels ? (
          <div className="flex items-center gap-3 rounded-xl border border-sidebar-border/70 bg-card/28 p-3">
            <Avatar className="size-9">
              <AvatarFallback className="bg-primary/15 text-[11px] font-semibold text-primary">
                {getInitials(user.displayName, user.email)}
              </AvatarFallback>
            </Avatar>
            <div className="min-w-0 flex-1">
              <div className="truncate text-sm font-medium">
                {user.displayName || "Signed in"}
              </div>
              <div className="truncate text-[11px] text-muted-foreground">
                {visibleRoles}
              </div>
            </div>
          </div>
        ) : null}

        {onLogout ? (
          collapsed && !compact ? (
            <Tooltip>
              <TooltipTrigger
                render={
                  <button
                    type="button"
                    className="flex min-h-11 w-full items-center justify-center rounded-xl border border-transparent px-2 py-2.5 text-tone-danger-foreground transition-colors hover:border-tone-danger-border hover:bg-tone-danger"
                    onClick={onLogout}
                    aria-label="Logout"
                  />
                }
              >
                <LogOut className="size-4" />
              </TooltipTrigger>
              <TooltipContent side="right">Logout</TooltipContent>
            </Tooltip>
          ) : (
            <button
              type="button"
              className="group flex min-h-11 w-full items-center gap-3 rounded-xl border border-transparent px-3 py-2.5 text-sm text-tone-danger-foreground transition-colors hover:border-tone-danger-border hover:bg-tone-danger"
              onClick={onLogout}
            >
              <span className="flex size-8 shrink-0 items-center justify-center rounded-lg border border-tone-danger-border/70 bg-tone-danger/60">
                <LogOut className="size-4" />
              </span>
              <span className="tracking-tight">Logout</span>
            </button>
          )
        ) : null}
      </div>
    </aside>
  );
}
