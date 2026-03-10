import { createContext, useContext } from 'react'

export const selectedTenantStorageKey = 'patchhound:selected-tenant'

export type TenantScopeContextValue = {
  selectedTenantId: string | null
  tenants: Array<{ id: string; name: string }>
  isLoadingTenants: boolean
  setSelectedTenantId: (tenantId: string) => void
}

export const TenantScopeContext = createContext<TenantScopeContextValue | null>(null)

export function useTenantScope() {
  const context = useContext(TenantScopeContext)

  if (!context) {
    throw new Error('useTenantScope must be used within TenantScopeProvider')
  }

  return context
}
