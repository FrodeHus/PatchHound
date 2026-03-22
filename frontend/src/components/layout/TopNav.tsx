import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "@tanstack/react-router";
import { toast } from "sonner";
import {
  AlertTriangle,
  Menu,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
  ShieldCheck,
} from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Breadcrumbs } from "@/components/layout/Breadcrumbs";
import { NotificationBell } from "@/components/layout/NotificationBell";
import { OpenBaoUnsealDialog } from "@/components/layout/OpenBaoUnsealDialog";
import { TenantSelector } from "@/components/layout/TenantSelector";
import { useTenantScope } from "@/components/layout/tenant-scope";
import type { CurrentUser } from "@/server/auth.functions";
import { unsealOpenBao } from "@/server/system.functions";
import { ThemeSelector } from "@/components/layout/ThemeSelector";
import { readDashboardViewPreference, writeDashboardViewPreference, type DashboardViewMode } from "@/lib/dashboard-view";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";

type TopNavProps = {
  user: CurrentUser;
  onToggleSidebar: () => void;
  onToggleDesktopSidebar?: () => void;
  isDesktopSidebarCollapsed?: boolean;
  onLogout: () => void;
};

function getInitials(displayName: string, email: string) {
  const source = displayName.trim() || email.trim();
  return source
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("");
}

export function TopNav({
  user,
  onToggleSidebar,
  onToggleDesktopSidebar,
  isDesktopSidebarCollapsed,
  onLogout,
}: TopNavProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [isUnsealDialogOpen, setIsUnsealDialogOpen] = useState(false);
  const [isScrolled, setIsScrolled] = useState(false);
  const [dashboardViewMode, setDashboardViewMode] = useState<DashboardViewMode>("executive");
  const { selectedTenantId, setSelectedTenantId, tenants } = useTenantScope();
  const canUnsealOpenBao = user.roles.includes("GlobalAdmin");
  const canSwitchPortalView = user.roles.includes("GlobalAdmin");
  const unsealMutation = useMutation({
    mutationFn: (keys: [string, string, string]) =>
      unsealOpenBao({ data: { keys } }),
    onSuccess: async () => {
      toast.success("OpenBao unsealed");
      setIsUnsealDialogOpen(false);
      await router.invalidate();
    },
    onError: () => {
      toast.error("Failed to unseal OpenBao");
    },
  });

  useEffect(() => {
    const handleScroll = () => {
      setIsScrolled(window.scrollY > 8);
    };
    window.addEventListener("scroll", handleScroll, { passive: true });
    return () => window.removeEventListener("scroll", handleScroll);
  }, []);

  useEffect(() => {
    if (!canSwitchPortalView) {
      return;
    }

    setDashboardViewMode(readDashboardViewPreference() ?? "executive");
  }, [canSwitchPortalView]);

  const handlePortalViewChange = (nextMode: DashboardViewMode) => {
    setDashboardViewMode(nextMode);
    writeDashboardViewPreference(nextMode);

    const pathname = router.state.location.pathname;
    if (pathname === "/") {
      void router.navigate({
        to: "/",
        search: (prev: Record<string, unknown>) => ({ ...prev, mode: nextMode }),
      });
    }
  };

  return (
    <header
      className={[
        "sticky top-0 z-20 border-b transition-all duration-200",
        isScrolled
          ? "border-border/50 shadow-[0_1px_12px_rgba(0,0,0,0.12)]"
          : "border-transparent",
      ].join(" ")}
      style={{
        background:
          "color-mix(in oklab, var(--color-background) 78%, transparent)",
        backdropFilter: "blur(16px) saturate(1.3)",
        WebkitBackdropFilter: "blur(16px) saturate(1.3)",
      }}
    >
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 sm:px-6">
        <div className="flex items-center gap-3">
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="rounded-lg md:hidden"
            aria-label="Toggle menu"
            onClick={onToggleSidebar}
          >
            <Menu className="size-4" />
          </Button>

          {onToggleDesktopSidebar ? (
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="hidden rounded-lg text-muted-foreground hover:text-foreground md:inline-flex"
              aria-label={
                isDesktopSidebarCollapsed
                  ? "Expand sidebar"
                  : "Collapse sidebar"
              }
              onClick={onToggleDesktopSidebar}
            >
              {isDesktopSidebarCollapsed ? (
                <PanelLeftOpen className="size-4" />
              ) : (
                <PanelLeftClose className="size-4" />
              )}
            </Button>
          ) : null}

          <div className="flex items-center gap-3">
            <h1 className="text-base font-semibold tracking-tight text-foreground">
              Security posture command
            </h1>
            <div
              className={
                user.systemStatus?.openBaoSealed
                  ? "hidden items-center gap-1.5 rounded-full border border-tone-warning-border bg-tone-warning px-2.5 py-0.5 text-[11px] text-tone-warning-foreground sm:flex"
                  : "hidden items-center gap-1.5 rounded-full border border-tone-success-border bg-tone-success px-2.5 py-0.5 text-[11px] text-tone-success-foreground sm:flex"
              }
            >
              {user.systemStatus?.openBaoSealed ? (
                <AlertTriangle className="size-3" />
              ) : (
                <ShieldCheck className="size-3" />
              )}
              {user.systemStatus?.openBaoSealed
                ? "Ingest paused"
                : "Ingest active"}
            </div>
            {user.systemStatus?.openBaoSealed ? (
              <Tooltip>
                <TooltipTrigger
                  render={
                    <button
                      type="button"
                      className={[
                        "hidden items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-semibold transition-colors sm:inline-flex",
                        "border-tone-warning-border bg-tone-warning text-tone-warning-foreground",
                        canUnsealOpenBao
                          ? "cursor-pointer hover:brightness-95"
                          : "cursor-default",
                      ].join(" ")}
                      onClick={() => {
                        if (canUnsealOpenBao) {
                          setIsUnsealDialogOpen(true);
                        }
                      }}
                    />
                  }
                  disabled={!canUnsealOpenBao}
                >
                  <AlertTriangle className="size-3" />
                  OpenBao sealed
                </TooltipTrigger>
                <TooltipContent>
                  {canUnsealOpenBao
                    ? "OpenBao is sealed. Click to provide unseal keys and resume ingestion."
                    : "OpenBao is sealed. A Global Admin must unseal the vault to resume ingestion."}
                </TooltipContent>
              </Tooltip>
            ) : null}
          </div>
        </div>

        <div className="flex items-center gap-2">
          <TenantSelector
            tenants={tenants}
            selectedTenantId={selectedTenantId}
            onSelectTenant={(tenantId) => {
              if (tenantId === selectedTenantId) {
                return;
              }

              setSelectedTenantId(tenantId);
              void queryClient.invalidateQueries();
              void router.invalidate();
            }}
          />
          <NotificationBell />
          <DropdownMenu>
            <DropdownMenuTrigger
              render={
                <Button
                  variant="ghost"
                  className="h-9 gap-2 rounded-lg px-2 hover:bg-accent/60"
                />
              }
            >
              <Avatar className="size-7">
                <AvatarFallback className="bg-primary/15 text-[11px] font-semibold text-primary">
                  {getInitials(user.displayName, user.email)}
                </AvatarFallback>
              </Avatar>
              <div className="hidden text-left sm:block">
                <p className="text-sm font-medium leading-none">
                  {user.displayName || user.email}
                </p>
                <p className="mt-0.5 text-[11px] text-muted-foreground">
                  {user.roles[0] ?? "Member"}
                </p>
              </div>
            </DropdownMenuTrigger>
            <DropdownMenuContent
              align="end"
              className="w-64 rounded-xl border-border/70 bg-popover/95 p-1.5 backdrop-blur"
            >
              <DropdownMenuGroup>
                <DropdownMenuLabel className="px-3 py-2">
                  <div className="space-y-1">
                    <p className="text-sm font-medium text-foreground">
                      {user.displayName || "Signed in"}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {user.email}
                    </p>
                  </div>
                </DropdownMenuLabel>
              </DropdownMenuGroup>
              <DropdownMenuSeparator />
              <div className="px-2 py-1.5">
                <ThemeSelector />
              </div>
              <DropdownMenuSeparator />
              {canSwitchPortalView ? (
                <>
                  <DropdownMenuGroup>
                    <DropdownMenuLabel className="px-3 pb-1 pt-2 text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                      Portal view
                    </DropdownMenuLabel>
                    <DropdownMenuRadioGroup
                      value={dashboardViewMode}
                      onValueChange={(value) =>
                        handlePortalViewChange(value as DashboardViewMode)
                      }
                    >
                      <DropdownMenuRadioItem
                        value="executive"
                        className="rounded-lg px-3 py-2"
                      >
                        Executive overview
                      </DropdownMenuRadioItem>
                      <DropdownMenuRadioItem
                        value="operations"
                        className="rounded-lg px-3 py-2"
                      >
                        Operations workbench
                      </DropdownMenuRadioItem>
                    </DropdownMenuRadioGroup>
                  </DropdownMenuGroup>
                  <DropdownMenuSeparator />
                </>
              ) : null}
              <DropdownMenuItem className="rounded-lg px-3 py-2">
                {user.tenantIds.length} tenant scope
                {user.tenantIds.length === 1 ? "" : "s"}
              </DropdownMenuItem>
              <DropdownMenuItem
                className="rounded-lg px-3 py-2"
                onClick={onLogout}
              >
                <LogOut className="size-4" />
                Logout
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>
      <div className="px-4 pb-2 sm:px-6">
        <Breadcrumbs />
      </div>
      <OpenBaoUnsealDialog
        isOpen={isUnsealDialogOpen}
        isSubmitting={unsealMutation.isPending}
        errorMessage={
          unsealMutation.error instanceof Error
            ? unsealMutation.error.message
            : null
        }
        onClose={() => {
          if (!unsealMutation.isPending) {
            setIsUnsealDialogOpen(false);
            unsealMutation.reset();
          }
        }}
        onSubmit={(keys) => {
          unsealMutation.mutate(keys);
        }}
      />
    </header>
  );
}
