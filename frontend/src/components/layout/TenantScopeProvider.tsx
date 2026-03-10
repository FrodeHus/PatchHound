import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchTenants } from '@/api/settings.functions'
import type { TenantListItem } from '@/api/settings.schemas'
import type { CurrentUser } from '@/server/auth.functions'
import { selectedTenantStorageKey, TenantScopeContext, type TenantScopeContextValue } from '@/components/layout/tenant-scope'

function getInitialTenantId(): string | null {
  if (typeof window === 'undefined') return null
  return window.localStorage.getItem(selectedTenantStorageKey)
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
    if (typeof window === 'undefined') {
      return
    }

    if (effectiveSelectedTenantId) {
      window.localStorage.setItem(selectedTenantStorageKey, effectiveSelectedTenantId)
      return
    }

    window.localStorage.removeItem(selectedTenantStorageKey)
  }, [effectiveSelectedTenantId])

  const value = useMemo<TenantScopeContextValue>(() => ({
    selectedTenantId: effectiveSelectedTenantId,
    tenants,
    isLoadingTenants: tenantQuery.isPending,
    setSelectedTenantId: (tenantId: string) => {
      setStoredTenantId(tenantId)
      if (typeof window !== 'undefined') {
        window.localStorage.setItem(selectedTenantStorageKey, tenantId)
      }
    },
  }), [effectiveSelectedTenantId, tenantQuery.isPending, tenants])

  return (
    <TenantScopeContext.Provider value={value}>
      {children}
    </TenantScopeContext.Provider>
  )
}
