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
        const frontendOrigin = process.env.FRONTEND_ORIGIN ?? 'http://localhost:3000'

        let validatedOrigin: string
        try {
          const parsed = new URL(frontendOrigin)
          if (process.env.NODE_ENV === 'production' && parsed.protocol !== 'https:') {
            throw new Error('FRONTEND_ORIGIN must use HTTPS in production')
          }
          validatedOrigin = parsed.origin
        } catch {
          validatedOrigin = 'http://localhost:3000'
        }

        const postLogoutUri = encodeURIComponent(validatedOrigin)
        const logoutUrl = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/logout?post_logout_redirect_uri=${postLogoutUri}`

        return redirectResponse(logoutUrl)
      },
    },
  },
})
