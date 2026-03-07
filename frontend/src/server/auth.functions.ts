import { createServerFn } from '@tanstack/react-start'
import { getSession } from '@/server/session'

export const getCurrentUser = createServerFn({ method: 'GET' })
  .handler(async () => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      return null
    }

    return {
      id: session.userId,
      email: session.email ?? '',
      displayName: session.displayName ?? '',
      roles: session.roles ?? [],
      tenantId: session.tenantId,
      tenantIds: session.tenantIds ?? (session.tenantId ? [session.tenantId] : []),
    }
  })

export type CurrentUser = NonNullable<Awaited<ReturnType<typeof getCurrentUser>>>
