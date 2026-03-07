import { Menu } from 'lucide-react'
import type { CurrentUser } from '@/types/api'
import { NotificationBell } from '@/components/layout/NotificationBell'
import { TenantSelector } from '@/components/layout/TenantSelector'

type TopNavProps = {
  user: CurrentUser | null
  selectedTenantId: string | null
  onSelectTenant: (tenantId: string) => void
  onToggleSidebar: () => void
  onLogin: () => void
  onLogout: () => void
}

export function TopNav({
  user,
  selectedTenantId,
  onSelectTenant,
  onToggleSidebar,
  onLogin,
  onLogout,
}: TopNavProps) {
  return (
    <header className="sticky top-0 z-20 flex h-16 items-center justify-between border-b border-border bg-background/95 px-4 backdrop-blur">
      <div className="flex items-center gap-4">
        <button
          type="button"
          className="rounded-md p-2 hover:bg-muted md:hidden"
          aria-label="Toggle menu"
          onClick={onToggleSidebar}
        >
          <Menu size={18} />
        </button>
        <TenantSelector
          tenants={user?.tenants ?? []}
          selectedTenantId={selectedTenantId}
          onSelectTenant={onSelectTenant}
        />
      </div>

      <div className="flex items-center gap-3">
        <NotificationBell />
        <div className="hidden text-right text-sm sm:block">
          <p className="font-medium">{user?.displayName ?? 'Guest'}</p>
          <p className="text-xs text-muted-foreground">{user?.email ?? 'Not signed in'}</p>
        </div>
        {user ? (
          <button
            type="button"
            className="rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted"
            onClick={onLogout}
          >
            Logout
          </button>
        ) : (
          <button
            type="button"
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
            onClick={onLogin}
          >
            Login
          </button>
        )}
      </div>
    </header>
  )
}
