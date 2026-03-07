import { createMiddleware } from '@tanstack/react-start'
import { getSession, isTokenExpired } from '@/server/session'
import { refreshAccessToken } from '@/server/auth'
import { normalizeRoles } from '@/server/roles'
import { redirect } from '@tanstack/react-router'

export const authMiddleware = createMiddleware({ type: 'function' })
  .server(async ({ next }) => {
    const session = await getSession()

    if (!session.accessToken) {
      throw redirect({ to: '/auth/login' })
    }

    // Refresh token if expired
    if (isTokenExpired(session) && session.homeAccountId) {
      try {
        const tokens = await refreshAccessToken(session.homeAccountId)
        session.accessToken = tokens.access_token
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
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

    const roles = normalizeRoles(session.roles)
    if ((session.roles ?? []).join('|') !== roles.join('|')) {
      session.roles = roles
      await session.save()
    }

    return next({
      context: {
        token: session.accessToken,
        userId: session.userId,
        tenantId: session.tenantId,
        roles,
      },
    })
  })
