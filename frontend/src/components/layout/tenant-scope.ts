import { createContext, useContext } from 'react'

export const selectedTenantStorageKey = 'patchhound:selected-tenant'
export const selectedTenantCookieKey = 'patchhound-selected-tenant'
const selectedTenantCookieMaxAgeSeconds = 60 * 60 * 24 * 365

export type TenantScopeContextValue = {
  selectedTenantId: string | null
  tenants: Array<{ id: string; name: string }>
  isLoadingTenants: boolean
  setSelectedTenantId: (tenantId: string) => void
  tenantPendingDeletion: boolean
  clearTenantPendingDeletion: () => void
}

export const TenantScopeContext = createContext<TenantScopeContextValue | null>(null)

export function useTenantScope() {
  const context = useContext(TenantScopeContext)

  if (!context) {
    throw new Error('useTenantScope must be used within TenantScopeProvider')
  }

  return context
}

export function persistSelectedTenant(tenantId: string | null) {
  if (typeof window === 'undefined') {
    return
  }

  if (tenantId) {
    window.localStorage.setItem(selectedTenantStorageKey, tenantId)
    document.cookie = [
      `${selectedTenantCookieKey}=${encodeURIComponent(tenantId)}`,
      'Path=/',
      `Max-Age=${selectedTenantCookieMaxAgeSeconds}`,
      'SameSite=Lax',
    ].join('; ')
    return
  }

  window.localStorage.removeItem(selectedTenantStorageKey)
  document.cookie = [
    `${selectedTenantCookieKey}=`,
    'Path=/',
    'Max-Age=0',
    'SameSite=Lax',
  ].join('; ')
}
