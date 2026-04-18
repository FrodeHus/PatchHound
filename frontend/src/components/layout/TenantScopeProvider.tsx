import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { fetchTenants } from '@/api/settings.functions'
import type { TenantListItem } from '@/api/settings.schemas'
import type { CurrentUser } from '@/server/auth.functions'
import { useSSE } from '@/hooks/useSSE'
import { isTenantPendingDeletion } from '@/lib/tenant-deletion'
import {
  persistSelectedTenant,
  selectedTenantCookieKey,
  selectedTenantStorageKey,
  TenantScopeContext,
  type TenantScopeContextValue,
} from '@/components/layout/tenant-scope'

function getInitialTenantId(): string | null {
  if (typeof window === 'undefined') return null

  const storedTenantId = window.localStorage.getItem(selectedTenantStorageKey)
  if (storedTenantId) {
    return storedTenantId
  }

  const cookieValue = document.cookie
    .split('; ')
    .find((entry) => entry.startsWith(`${selectedTenantCookieKey}=`))
    ?.slice(selectedTenantCookieKey.length + 1)

  return cookieValue ? decodeURIComponent(cookieValue) : null
}

function buildTenantOptions(user: CurrentUser, tenantItems: TenantListItem[] | undefined) {
  if (tenantItems && tenantItems.length > 0) {
    return tenantItems.map((tenant) => ({
      id: tenant.id,
      name: tenant.name,
    }))
  }

  const allowedIds = user.tenantIds.length ? user.tenantIds : []

  return allowedIds.map((tenantId, index) => ({
    id: tenantId,
    name: `Tenant ${index + 1}`,
  }))
}

type TenantScopeProviderProps = {
  user: CurrentUser
  children: ReactNode
}

export function TenantScopeProvider({ user, children }: TenantScopeProviderProps) {
  const [storedTenantId, setStoredTenantId] = useState<string | null>(getInitialTenantId)
  const [tenantPendingDeletion, setTenantPendingDeletion] = useState(false)
  const queryClient = useQueryClient()
  const tenantQuery = useQuery({
    queryKey: ['tenant-scope', 'tenants'],
    queryFn: () => fetchTenants({ data: { page: 1, pageSize: 100 } }),
  })

  const tenants = useMemo(
    () => buildTenantOptions(user, tenantQuery.data?.items),
    [tenantQuery.data?.items, user],
  )
  const effectiveSelectedTenantId = useMemo(() => {
    if (storedTenantId && tenants.some((tenant) => tenant.id === storedTenantId)) {
      return storedTenantId
    }

    return tenants[0]?.id ?? null
  }, [storedTenantId, tenants])

  useEffect(() => {
    persistSelectedTenant(effectiveSelectedTenantId)
  }, [effectiveSelectedTenantId])

  useEffect(() => {
    return queryClient.getQueryCache().subscribe((event) => {
      if (event.type === 'updated' && event.query.state.status === 'error') {
        if (isTenantPendingDeletion(event.query.state.error)) {
          setTenantPendingDeletion(true)
        }
      }
    })
  }, [queryClient])

  useSSE('TenantDeleted', (data) => {
    const payload = data as { tenantId?: string }
    void queryClient.invalidateQueries({ queryKey: ['tenant-scope', 'tenants'] })
    if (payload?.tenantId === effectiveSelectedTenantId) {
      setTenantPendingDeletion(true)
    } else {
      toast.success('Tenant deleted successfully.')
    }
  })

  useSSE('TenantDeletionFailed', (data) => {
    const payload = data as { tenantId?: string }
    void queryClient.invalidateQueries({ queryKey: ['tenant-scope', 'tenants'] })
    if (payload?.tenantId === effectiveSelectedTenantId) {
      toast.error('Tenant deletion failed. Please contact an administrator.')
    }
  })

  const value = useMemo<TenantScopeContextValue>(() => ({
    selectedTenantId: effectiveSelectedTenantId,
    tenants,
    isLoadingTenants: tenantQuery.isPending,
    setSelectedTenantId: (tenantId: string) => {
      setStoredTenantId(tenantId)
      persistSelectedTenant(tenantId)
    },
    tenantPendingDeletion,
    clearTenantPendingDeletion: () => setTenantPendingDeletion(false),
  }), [effectiveSelectedTenantId, tenantQuery.isPending, tenants, tenantPendingDeletion])

  return (
    <TenantScopeContext.Provider value={value}>
      {children}
    </TenantScopeContext.Provider>
  )
}
