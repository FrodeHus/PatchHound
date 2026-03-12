import { createFileRoute } from '@tanstack/react-router'
import { redirectResponse } from '@/server/http'
import { getSession } from '@/server/session'

export const Route = createFileRoute('/auth/logout')({
  server: {
    handlers: {
      GET: async () => {
        const session = await getSession()
        await session.destroy()

        const tenantId = process.env.ENTRA_TENANT_ID
        const postLogoutUri = encodeURIComponent(process.env.FRONTEND_ORIGIN ?? 'http://localhost:3000')
        const logoutUrl = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/logout?post_logout_redirect_uri=${postLogoutUri}`

        return redirectResponse(logoutUrl)
      },
    },
  },
})
