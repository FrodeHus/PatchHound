import { createServerFn } from '@tanstack/react-start'
import { apiGet } from '@/server/api'
import { getSession } from '@/server/session'

type SystemStatus = {
  openBaoAvailable: boolean
  openBaoInitialized: boolean
  openBaoSealed: boolean
}

export const getCurrentUser = createServerFn({ method: 'GET' })
  .handler(async () => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      return null
    }

    let systemStatus: SystemStatus | null = null
    try {
      systemStatus = await apiGet<SystemStatus>('/system/status', session.accessToken)
    } catch {
      systemStatus = null
    }

    return {
      id: session.userId,
      email: session.email ?? '',
      displayName: session.displayName ?? '',
      roles: session.roles ?? [],
      tenantId: session.tenantId,
      tenantIds: session.tenantIds ?? (session.tenantId ? [session.tenantId] : []),
      systemStatus,
    }
  })

export type CurrentUser = NonNullable<Awaited<ReturnType<typeof getCurrentUser>>>
