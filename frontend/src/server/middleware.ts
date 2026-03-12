import { createMiddleware } from '@tanstack/react-start'
import { getCookie } from '@tanstack/react-start/server'
import { getSession, isTokenExpired } from '@/server/session'
import { hydrateTokenCache, refreshAccessToken } from '@/server/auth'
import { redirect } from '@tanstack/react-router'
import { selectedTenantCookieKey } from '@/components/layout/tenant-scope'

function resolveActiveTenantId(session: Awaited<ReturnType<typeof getSession>>) {
  const accessibleTenantIds = session.tenantIds ?? (session.tenantId ? [session.tenantId] : [])
  const selectedTenantId = getCookie(selectedTenantCookieKey)

  if (selectedTenantId && accessibleTenantIds.includes(selectedTenantId)) {
    return {
      activeTenantId: selectedTenantId,
      accessibleTenantIds,
    }
  }

  return {
    activeTenantId: accessibleTenantIds[0],
    accessibleTenantIds,
  }
}

export const authMiddleware = createMiddleware({ type: 'function' })
  .server(async ({ next }) => {
    const session = await getSession()

    if (!session.accessToken) {
      throw redirect({ to: '/auth/login' })
    }

    // Refresh token if expired
    if (isTokenExpired(session) && session.homeAccountId) {
      try {
        await hydrateTokenCache(session.tokenCache)
        const tokens = await refreshAccessToken(session.homeAccountId)
        session.accessToken = tokens.access_token
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
        session.tokenCache = tokens.token_cache
        await session.save()
      } catch {
        // Refresh failed — force re-login
        session.destroy()
        throw redirect({ to: '/auth/login' })
      }
    } else if (isTokenExpired(session)) {
      session.destroy()
      throw redirect({ to: '/auth/login' })
    }

    const { activeTenantId, accessibleTenantIds } = resolveActiveTenantId(session)

    return next({
      context: {
        token: session.accessToken,
        userId: session.userId,
        tenantId: activeTenantId,
        activeTenantId,
        accessibleTenantIds,
        roles: session.roles ?? [],
      },
    })
  })
