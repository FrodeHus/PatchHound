import { createElement, type ReactNode } from 'react'
import { MsalProvider } from '@azure/msal-react'
import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
  type Configuration,
  type PopupRequest,
} from '@azure/msal-browser'
import type { CurrentUser, RoleName, TenantAccess } from '@/types/api'

const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID
const authority = import.meta.env.VITE_ENTRA_AUTHORITY ?? 'https://login.microsoftonline.com/common'
const redirectUri = import.meta.env.VITE_ENTRA_REDIRECT_URI ?? window.location.origin
const postLogoutRedirectUri = import.meta.env.VITE_ENTRA_POST_LOGOUT_REDIRECT_URI ?? window.location.origin
const scopes = (import.meta.env.VITE_API_SCOPES as string | undefined)?.split(' ').filter(Boolean) ?? []

const msalConfiguration: Configuration | null = clientId
  ? {
      auth: {
        clientId,
        authority,
        redirectUri,
        postLogoutRedirectUri,
      },
      cache: {
        cacheLocation: 'localStorage',
      },
    }
  : null

const msalInstance = msalConfiguration ? new PublicClientApplication(msalConfiguration) : null
let isInitialized = false

function toStringArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.filter((item): item is string => typeof item === 'string')
  }

  if (typeof value === 'string') {
    return [value]
  }

  return []
}

function toString(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

async function ensureInitialized(): Promise<void> {
  if (!msalInstance || isInitialized) {
    return
  }

  await msalInstance.initialize()
  isInitialized = true
}

function getAccount(): AccountInfo | null {
  if (!msalInstance) {
    return null
  }

  const active = msalInstance.getActiveAccount()
  if (active) {
    return active
  }

  const allAccounts = msalInstance.getAllAccounts()
  if (allAccounts.length === 0) {
    return null
  }

  const firstAccount = allAccounts[0]
  msalInstance.setActiveAccount(firstAccount)
  return firstAccount
}

function buildLoginRequest(): PopupRequest {
  return {
    scopes,
  }
}

function parseUserFromAccount(account: AccountInfo | null): CurrentUser | null {
  if (!account) {
    return null
  }

  const claims = account.idTokenClaims
  const roleValues = toStringArray(claims?.roles)
  const roles = roleValues.filter((role): role is RoleName => {
    return (
      role === 'GlobalAdmin' ||
      role === 'SecurityManager' ||
      role === 'SecurityAnalyst' ||
      role === 'AssetOwner' ||
      role === 'Stakeholder' ||
      role === 'Auditor'
    )
  })

  const tenantIds = toStringArray(claims?.tenant_ids)
  const primaryTenant = toString(claims?.tid)
  const resolvedTenantIds = tenantIds.length > 0 ? tenantIds : primaryTenant ? [primaryTenant] : []

  const tenants: TenantAccess[] = resolvedTenantIds.map((tenantId) => ({
    id: tenantId,
    name: tenantId,
  }))

  return {
    id: account.homeAccountId,
    email: account.username,
    displayName: account.name ?? account.username,
    roles,
    tenants,
    isCrossTenant: tenants.length > 1,
  }
}

export async function getAccessToken(): Promise<string | null> {
  if (!msalInstance) {
    return null
  }

  await ensureInitialized()
  const account = getAccount()
  if (!account) {
    return null
  }

  try {
    const result = await msalInstance.acquireTokenSilent({
      account,
      scopes,
    })
    return result.accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      await msalInstance.loginRedirect(buildLoginRequest())
      return null
    }

    throw error
  }
}

export async function login(): Promise<void> {
  if (!msalInstance) {
    return
  }

  await ensureInitialized()
  await msalInstance.loginRedirect(buildLoginRequest())
}

export async function logout(): Promise<void> {
  if (!msalInstance) {
    return
  }

  await ensureInitialized()
  await msalInstance.logoutRedirect()
}

export async function getCurrentUser(): Promise<CurrentUser | null> {
  if (!msalInstance) {
    return {
      id: 'local-dev-user',
      email: 'local@example.com',
      displayName: 'Local Developer',
      roles: ['GlobalAdmin'],
      tenants: [{ id: 'local-tenant', name: 'Local Tenant' }],
      isCrossTenant: false,
    }
  }

  await ensureInitialized()
  return parseUserFromAccount(getAccount())
}

export function AuthProvider({ children }: { children: ReactNode }): ReactNode {
  if (!msalInstance) {
    return children
  }

  return createElement(MsalProvider, { instance: msalInstance }, children)
}
