import { createFileRoute } from '@tanstack/react-router'
import { getAuthorizationUrl } from '@/server/auth'
import { getSession } from '@/server/session'

export const Route = createFileRoute('/auth/login')({
  server: {
    handlers: {
      GET: async () => {
        const session = await getSession()
        const state = crypto.randomUUID()
        session.oauthState = state
        await session.save()

        const url = await getAuthorizationUrl(state)
        return Response.redirect(url, 302)
      },
    },
  },
})
