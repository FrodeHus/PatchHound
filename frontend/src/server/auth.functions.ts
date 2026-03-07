import { createServerFn } from '@tanstack/react-start'
import { normalizeRoles } from '@/server/roles'
import { getSession } from '@/server/session'

export const getCurrentUser = createServerFn({ method: 'GET' })
  .handler(async () => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      return null
    }

    const roles = normalizeRoles(session.roles)
    if ((session.roles ?? []).join('|') !== roles.join('|')) {
      session.roles = roles
      await session.save()
    }

    return {
      id: session.userId,
      email: session.email ?? '',
      displayName: session.displayName ?? '',
      roles,
      tenantId: session.tenantId,
      tenantIds: session.tenantIds ?? (session.tenantId ? [session.tenantId] : []),
    }
  })

export type CurrentUser = NonNullable<Awaited<ReturnType<typeof getCurrentUser>>>
