import { useEffect, useState, type ReactNode } from 'react'
import { Sidebar } from '@/components/layout/Sidebar'
import { TopNav } from '@/components/layout/TopNav'
import { useAuthActions, useCurrentUser } from '@/hooks/useCurrentUser'

const selectedTenantStorageKey = 'vigil:selected-tenant'

type AppShellProps = {
  children: ReactNode
}

function getInitialTenantId(): string | null {
  return window.localStorage.getItem(selectedTenantStorageKey)
}

export function AppShell({ children }: AppShellProps) {
  const [isSidebarOpen, setIsSidebarOpen] = useState(false)
  const [selectedTenantId, setSelectedTenantId] = useState<string | null>(getInitialTenantId)
  const { data: user } = useCurrentUser()
  const { login, logout } = useAuthActions()
  const effectiveSelectedTenantId = selectedTenantId ?? user?.tenants[0]?.id ?? null

  useEffect(() => {
    if (!selectedTenantId && effectiveSelectedTenantId) {
      window.localStorage.setItem(selectedTenantStorageKey, effectiveSelectedTenantId)
    }
  }, [effectiveSelectedTenantId, selectedTenantId])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <div className="flex min-h-screen">
        <Sidebar
          user={user ?? null}
          isOpen={isSidebarOpen}
          onNavigate={() => {
            setIsSidebarOpen(false)
          }}
        />
        <div className="flex min-h-screen min-w-0 flex-1 flex-col">
          <TopNav
            user={user ?? null}
            selectedTenantId={effectiveSelectedTenantId}
            onSelectTenant={(tenantId) => {
              setSelectedTenantId(tenantId)
              window.localStorage.setItem(selectedTenantStorageKey, tenantId)
            }}
            onToggleSidebar={() => {
              setIsSidebarOpen((currentValue) => !currentValue)
            }}
            onLogin={() => {
              void login()
            }}
            onLogout={() => {
              void logout()
            }}
          />
          <main className="flex-1 p-4 sm:p-6">{children}</main>
        </div>
      </div>
    </div>
  )
}
