import { createFileRoute } from '@tanstack/react-router'
import { getAuthorizationUrl } from '@/server/auth'

export const Route = createFileRoute('/auth/login')({
  server: {
    handlers: {
      GET: async () => {
        const state = crypto.randomUUID()
        const url = getAuthorizationUrl(state)
        return Response.redirect(url, 302)
      },
    },
  },
})
