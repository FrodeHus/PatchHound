import { createFileRoute } from '@tanstack/react-router'
import { exchangeCodeForTokens } from '@/server/auth'
import { redirectResponse } from '@/server/http'
import { normalizeRoles } from '@/server/roles'
import { getSession } from '@/server/session'

export const Route = createFileRoute('/auth/callback')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const callbackUrl = new URL(request.url)
        const code = callbackUrl.searchParams.get('code')
        const state = callbackUrl.searchParams.get('state')
        const error = callbackUrl.searchParams.get('error')
        const redirectTo = (path: string) => redirectResponse(new URL(path, callbackUrl).toString())

        const session = await getSession()

        if (error || !code) {
          return redirectTo('/?error=auth_failed')
        }

        if (!state || state !== session.oauthState) {
          return redirectTo('/?error=invalid_auth_state')
        }

        let tokens: Awaited<ReturnType<typeof exchangeCodeForTokens>>
        try {
          tokens = await exchangeCodeForTokens(code)
        } catch {
          return redirectTo('/?error=token_exchange_failed')
        }

        if (!tokens.claims) {
          return redirectTo('/?error=missing_id_token_claims')
        }

        const claims = tokens.claims
        session.accessToken = tokens.access_token
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
        session.homeAccountId = tokens.home_account_id
        session.userId = claims.oid
        session.email = claims.preferred_username
        session.displayName = claims.name
        session.tenantId = claims.tid
        session.roles = normalizeRoles(claims.roles)
        session.oauthState = undefined
        await session.save()

        return redirectTo('/')
      },
    },
  },
})
