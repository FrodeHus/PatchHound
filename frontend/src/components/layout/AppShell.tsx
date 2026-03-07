import { useEffect, useState, type ReactNode } from 'react'
import { Sidebar } from '@/components/layout/Sidebar'
import { TopNav } from '@/components/layout/TopNav'
import { Sheet, SheetContent } from '@/components/ui/sheet'
import type { CurrentUser } from '@/server/auth.functions'

const selectedTenantStorageKey = 'vigil:selected-tenant'

type AppShellProps = {
  user: CurrentUser
  children: ReactNode
}

function getInitialTenantId(): string | null {
  if (typeof window === 'undefined') return null
  return window.localStorage.getItem(selectedTenantStorageKey)
}

export function AppShell({ user, children }: AppShellProps) {
  const [isSidebarOpen, setIsSidebarOpen] = useState(false)
  const [selectedTenantId, setSelectedTenantId] = useState<string | null>(getInitialTenantId)
  const effectiveSelectedTenantId = selectedTenantId ?? user.tenantIds[0] ?? null

  useEffect(() => {
    if (!selectedTenantId && effectiveSelectedTenantId) {
      window.localStorage.setItem(selectedTenantStorageKey, effectiveSelectedTenantId)
    }
  }, [effectiveSelectedTenantId, selectedTenantId])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <div className="flex min-h-screen">
        <div className="sticky top-0 hidden h-screen md:block">
          <Sidebar user={user} />
        </div>

        <Sheet open={isSidebarOpen} onOpenChange={setIsSidebarOpen}>
          <SheetContent side="left" className="w-[22rem] border-r border-sidebar-border/80 bg-sidebar/94 p-0 text-sidebar-foreground sm:max-w-[22rem]">
            <Sidebar
              user={user}
              compact
              onNavigate={() => {
                setIsSidebarOpen(false)
              }}
            />
          </SheetContent>
        </Sheet>

        <div className="flex min-h-screen min-w-0 flex-1 flex-col">
          <TopNav
            user={user}
            selectedTenantId={effectiveSelectedTenantId}
            onSelectTenant={(tenantId) => {
              setSelectedTenantId(tenantId)
              window.localStorage.setItem(selectedTenantStorageKey, tenantId)
            }}
            onToggleSidebar={() => {
              setIsSidebarOpen((currentValue) => !currentValue)
            }}
            onLogout={() => {
              window.location.href = '/auth/logout'
            }}
          />
          <main className="flex-1 px-4 pb-6 sm:px-6">
            <div className="mx-auto w-full max-w-[1600px]">
              {children}
            </div>
          </main>
        </div>
      </div>
    </div>
  )
}
