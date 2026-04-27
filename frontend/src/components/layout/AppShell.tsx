import { useState, type ReactNode } from 'react'
import { useRouterState } from '@tanstack/react-router'
import { AdminConsoleLayout } from '@/components/features/admin/AdminConsoleLayout'
import { Sidebar } from '@/components/layout/Sidebar'
import { TenantScopeProvider } from '@/components/layout/TenantScopeProvider'
import { TenantUnavailableDialog } from '@/components/layout/TenantUnavailableDialog'
import { TopNav } from '@/components/layout/TopNav'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Sheet, SheetContent } from '@/components/ui/sheet'
import type { CurrentUser } from '@/server/auth.functions'

function TenantGuard({ children }: { children: ReactNode }) {
  const { tenantPendingDeletion, clearTenantPendingDeletion, tenants, selectedTenantId, setSelectedTenantId } = useTenantScope()
  const availableTenants = tenants.filter(t => t.id !== selectedTenantId)
  return (
    <>
      <TenantUnavailableDialog
        open={tenantPendingDeletion}
        tenants={availableTenants}
        onSelectTenant={(id) => {
          setSelectedTenantId(id)
          clearTenantPendingDeletion()
        }}
      />
      {children}
    </>
  )
}

const sidebarStorageKey = "patchhound:sidebar-collapsed";

function getInitialSidebarCollapsed(): boolean {
  if (typeof window === "undefined") return false;
  return window.sessionStorage.getItem(sidebarStorageKey) === "true";
}

type AppShellProps = {
  user: CurrentUser
  children: ReactNode
}

export function AppShell({ user, children }: AppShellProps) {
  const [isSidebarOpen, setIsSidebarOpen] = useState(false)
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const isAdminRoute = pathname === '/admin' || pathname.startsWith('/admin/')
  const [isDesktopCollapsed, setIsDesktopCollapsed] = useState(
    getInitialSidebarCollapsed,
  );

  const toggleDesktopSidebar = () => {
    setIsDesktopCollapsed((prev) => {
      const next = !prev;
      try {
        window.sessionStorage.setItem(sidebarStorageKey, String(next));
      } catch {
        /* ignore */
      }
      return next;
    });
  };

  return (
    <TenantScopeProvider user={user}>
      <TenantGuard>
      <div className="min-h-screen bg-background text-foreground">
        <div className="flex min-h-screen">
          <div className="sticky top-0 hidden h-dvh md:block">
            <Sidebar
              user={user}
              collapsed={isDesktopCollapsed}
              onLogout={() => {
                window.location.href = "/auth/logout";
              }}
            />
          </div>

          <Sheet open={isSidebarOpen} onOpenChange={setIsSidebarOpen}>
            <SheetContent
              side="left"
              className="w-[22rem] border-r border-sidebar-border/80 bg-sidebar/94 p-0 text-sidebar-foreground sm:max-w-[22rem]"
            >
              <Sidebar
                user={user}
                compact
                onLogout={() => {
                  window.location.href = "/auth/logout";
                }}
                onNavigate={() => {
                  setIsSidebarOpen(false);
                }}
              />
            </SheetContent>
          </Sheet>

          <div className="flex min-h-screen min-w-0 flex-1 flex-col">
            <TopNav
              user={user}
              onToggleSidebar={() => {
                setIsSidebarOpen((currentValue) => !currentValue);
              }}
              onToggleDesktopSidebar={toggleDesktopSidebar}
              isDesktopSidebarCollapsed={isDesktopCollapsed}
              onLogout={() => {
                window.location.href = "/auth/logout";
              }}
            />
            <main className="flex-1 px-4 pb-6 sm:px-6">
              <div className="mx-auto w-full max-w-[1600px]">
                {isAdminRoute ? (
                  <AdminConsoleLayout user={user}>{children}</AdminConsoleLayout>
                ) : (
                  children
                )}
              </div>
            </main>
          </div>
        </div>
      </div>
      </TenantGuard>
    </TenantScopeProvider>
  );
}
