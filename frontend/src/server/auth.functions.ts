import { createServerFn } from '@tanstack/react-start'
import { apiGet } from '@/server/api'
import { getSession, isTokenExpired } from '@/server/session'
import { refreshAccessTokenSilent } from '@/server/auth'

type SystemStatus = {
  openBaoAvailable: boolean
  openBaoInitialized: boolean
  openBaoSealed: boolean
}

type SetupStatus = {
  isInitialized: boolean
  requiresSetup: boolean
}

type TenantListResponse = {
  items: Array<{ id: string }>
}

type AssignedRolesResponse = {
  roles: string[]
}

export const getCurrentUser = createServerFn({ method: 'GET' })
  .handler(async () => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      return null
    }

    // Refresh token if expired or about to expire
    if (isTokenExpired(session) && session.homeAccountId) {
      try {
        const tokens = await refreshAccessTokenSilent(session.homeAccountId, session.msalCache)
        session.accessToken = tokens.access_token
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
        session.msalCache = tokens.msalCache
        await session.save()
      } catch {
        return null
      }
    } else if (isTokenExpired(session)) {
      return null
    }

    let systemStatus: SystemStatus | null = null
    let setupStatus: SetupStatus | null = null
    let roles = session.roles ?? []
    let tenantIds = session.tenantIds ?? (session.tenantId ? [session.tenantId] : [])
    try {
      systemStatus = await apiGet<SystemStatus>('/system/status', {
        token: session.accessToken,
        tenantId: session.tenantId,
      })
    } catch {
      systemStatus = null
    }

    try {
      setupStatus = await apiGet<SetupStatus>('/setup/status', {
        token: session.accessToken,
        tenantId: session.tenantId,
      })
    } catch {
      setupStatus = null
    }

    try {
      const assignedRoles = await apiGet<AssignedRolesResponse>('/roles/assigned', {
        token: session.accessToken,
        tenantId: session.tenantId,
      })
      if (assignedRoles.roles.length > 0) {
        roles = assignedRoles.roles
        session.roles = roles
        await session.save()
      }
    } catch {
      // Fall back to session roles from token claims
    }

    try {
      const tenantResponse = await apiGet<TenantListResponse>('/tenants?page=1&pageSize=100', {
        token: session.accessToken,
        tenantId: session.tenantId,
      })
      const nextTenantIds = tenantResponse.items.map((tenant) => tenant.id)
      if (nextTenantIds.length > 0) {
        tenantIds = nextTenantIds
        session.tenantIds = nextTenantIds
        await session.save()
      }
    } catch {
      tenantIds = session.tenantIds ?? (session.tenantId ? [session.tenantId] : [])
    }

    return {
      id: session.userId,
      email: session.email ?? '',
      displayName: session.displayName ?? '',
      roles,
      activeRoles: session.activeRoles ?? [],
      tenantId: session.tenantId,
      tenantIds,
      requiresSetup: setupStatus?.requiresSetup ?? false,
      systemStatus,
    }
  })

export type CurrentUser = NonNullable<Awaited<ReturnType<typeof getCurrentUser>>>
