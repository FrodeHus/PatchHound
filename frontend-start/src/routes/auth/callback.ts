import { createFileRoute } from '@tanstack/react-router'
import { exchangeCodeForTokens, parseIdToken } from '@/server/auth'
import { getSession } from '@/server/session'

export const Route = createFileRoute('/auth/callback')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const url = new URL(request.url)
        const code = url.searchParams.get('code')
        const error = url.searchParams.get('error')

        if (error || !code) {
          return Response.redirect('/?error=auth_failed', 302)
        }

        const tokens = await exchangeCodeForTokens(code)
        const claims = parseIdToken(tokens.id_token)

        const session = await getSession()
        session.accessToken = tokens.access_token
        session.refreshToken = tokens.refresh_token
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
        session.userId = claims.oid
        session.email = claims.preferred_username
        session.displayName = claims.name
        session.tenantId = claims.tid
        session.roles = claims.roles ?? []
        await session.save()

        return Response.redirect('/', 302)
      },
    },
  },
})
