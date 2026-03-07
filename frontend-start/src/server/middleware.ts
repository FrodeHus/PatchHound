import { createMiddleware } from '@tanstack/react-start'
import { getSession, isTokenExpired } from '@/server/session'
import { refreshAccessToken } from '@/server/auth'
import { redirect } from '@tanstack/react-router'

export const authMiddleware = createMiddleware({ type: 'function' })
  .server(async ({ next }) => {
    const session = await getSession()

    if (!session.accessToken) {
      throw redirect({ to: '/auth/login' })
    }

    // Refresh token if expired
    if (isTokenExpired(session) && session.refreshToken) {
      try {
        const tokens = await refreshAccessToken(session.refreshToken)
        session.accessToken = tokens.access_token
        if (tokens.refresh_token) {
          session.refreshToken = tokens.refresh_token
        }
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
        await session.save()
      } catch {
        // Refresh failed — force re-login
        session.destroy()
        throw redirect({ to: '/auth/login' })
      }
    }

    return next({
      context: {
        token: session.accessToken,
        userId: session.userId,
        tenantId: session.tenantId,
        roles: session.roles ?? [],
      },
    })
  })
