import { createServerFn } from '@tanstack/react-start'
import { apiGet } from '@/server/api'
import { getSession, isTokenExpired } from '@/server/session'
import { refreshAccessTokenSilent } from '@/server/auth'
import type { FeaturesResponse } from '@/api/feature-flags.schemas'

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

    const globalContext = { token: session.accessToken }
    let systemStatus: SystemStatus | null = null
    let setupStatus: SetupStatus | null = null
    let roles = session.roles ?? []
    let tenantIds = session.tenantIds ?? (session.tenantId ? [session.tenantId] : [])
    let featureFlags: FeaturesResponse = {}
    let selectedTenantId = session.tenantId
    let shouldSaveSession = false

    try {
      const [systemResult, setupResult, tenantResult, featureResult] = await Promise.allSettled([
        apiGet<SystemStatus>('/system/status', globalContext),
        apiGet<SetupStatus>('/setup/status', globalContext),
        apiGet<TenantListResponse>('/tenants?page=1&pageSize=100', globalContext),
        apiGet<FeaturesResponse>('/features', globalContext),
      ])

      systemStatus = systemResult.status === 'fulfilled' ? systemResult.value : null
      setupStatus = setupResult.status === 'fulfilled' ? setupResult.value : null
      featureFlags = featureResult.status === 'fulfilled' ? featureResult.value : {}

      if (tenantResult.status !== 'fulfilled') {
        throw tenantResult.reason
      }

      const tenantResponse = tenantResult.value
      const nextTenantIds = tenantResponse.items.map((tenant) => tenant.id)
      if (nextTenantIds.length > 0) {
        tenantIds = nextTenantIds
        selectedTenantId = selectedTenantId && nextTenantIds.includes(selectedTenantId)
          ? selectedTenantId
          : nextTenantIds[0]
        if (
          session.tenantId !== selectedTenantId
          || JSON.stringify(session.tenantIds ?? []) !== JSON.stringify(nextTenantIds)
        ) {
          session.tenantId = selectedTenantId
          session.tenantIds = nextTenantIds
          shouldSaveSession = true
        }
      }
    } catch {
      tenantIds = session.tenantIds ?? (session.tenantId ? [session.tenantId] : [])
    }

    if (selectedTenantId && tenantIds.includes(selectedTenantId)) {
      try {
        const assignedRoles = await apiGet<AssignedRolesResponse>('/roles/assigned', {
          token: session.accessToken,
          tenantId: selectedTenantId,
        })
        if (assignedRoles.roles.length > 0) {
          roles = assignedRoles.roles
          session.roles = roles
          shouldSaveSession = true
        }
      } catch {
        // Fall back to session roles from token claims
      }
    }

    if (shouldSaveSession) {
      await session.save()
    }

    return {
      id: session.userId,
      email: session.email ?? '',
      displayName: session.displayName ?? '',
      roles,
      activeRoles: session.activeRoles ?? [],
      tenantId: selectedTenantId,
      tenantIds,
      requiresSetup: setupStatus?.requiresSetup ?? false,
      systemStatus,
      featureFlags,
    }
  })

export type CurrentUser = NonNullable<Awaited<ReturnType<typeof getCurrentUser>>>
