import { useEffect, useState, type ReactNode } from 'react'
import { Sidebar } from '@/components/layout/Sidebar'
import { TopNav } from '@/components/layout/TopNav'
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
  const tenants = user.tenantIds?.map(id => ({ id, name: id })) ?? []
  const effectiveSelectedTenantId = selectedTenantId ?? tenants[0]?.id ?? null

  useEffect(() => {
    if (!selectedTenantId && effectiveSelectedTenantId) {
      window.localStorage.setItem(selectedTenantStorageKey, effectiveSelectedTenantId)
    }
  }, [effectiveSelectedTenantId, selectedTenantId])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <div className="flex min-h-screen">
        <Sidebar
          user={user}
          isOpen={isSidebarOpen}
          onNavigate={() => {
            setIsSidebarOpen(false)
          }}
        />
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
            onLogin={() => {
              window.location.href = '/auth/login'
            }}
            onLogout={() => {
              window.location.href = '/auth/logout'
            }}
          />
          <main className="flex-1 p-4 sm:p-6">{children}</main>
        </div>
      </div>
    </div>
  )
}
