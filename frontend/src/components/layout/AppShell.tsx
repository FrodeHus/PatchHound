import { useState, type ReactNode } from 'react'
import { Sidebar } from '@/components/layout/Sidebar'
import { TenantScopeProvider } from '@/components/layout/TenantScopeProvider'
import { TopNav } from '@/components/layout/TopNav'
import { Sheet, SheetContent } from '@/components/ui/sheet'
import type { CurrentUser } from '@/server/auth.functions'

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
      <div className="min-h-screen bg-background text-foreground">
        <div className="flex min-h-screen">
          <div className="sticky top-0 hidden h-screen md:block">
            <Sidebar user={user} collapsed={isDesktopCollapsed} />
          </div>

          <Sheet open={isSidebarOpen} onOpenChange={setIsSidebarOpen}>
            <SheetContent
              side="left"
              className="w-[22rem] border-r border-sidebar-border/80 bg-sidebar/94 p-0 text-sidebar-foreground sm:max-w-[22rem]"
            >
              <Sidebar
                user={user}
                compact
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
              <div className="mx-auto w-full max-w-[1600px]">{children}</div>
            </main>
          </div>
        </div>
      </div>
    </TenantScopeProvider>
  );
}
