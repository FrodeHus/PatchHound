import { createServerFn } from '@tanstack/react-start'
import { apiGet } from '@/server/api'
import { getSession } from '@/server/session'

type SystemStatus = {
  openBaoAvailable: boolean
  openBaoInitialized: boolean
  openBaoSealed: boolean
}

type SetupStatus = {
  isInitialized: boolean
  requiresSetup: boolean
}

export const getCurrentUser = createServerFn({ method: 'GET' })
  .handler(async () => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      return null
    }

    let systemStatus: SystemStatus | null = null
    let setupStatus: SetupStatus | null = null
    try {
      systemStatus = await apiGet<SystemStatus>('/system/status', {
        token: session.accessToken,
        tenantId: session.tenantId,
      })
    } catch {
      systemStatus = null
    }

    try {
      setupStatus = await apiGet<SetupStatus>('/setup/status', {
        token: session.accessToken,
        tenantId: session.tenantId,
      })
    } catch {
      setupStatus = null
    }

    return {
      id: session.userId,
      email: session.email ?? '',
      displayName: session.displayName ?? '',
      roles: session.roles ?? [],
      tenantId: session.tenantId,
      tenantIds: session.tenantIds ?? (session.tenantId ? [session.tenantId] : []),
      requiresSetup: setupStatus?.requiresSetup ?? false,
      systemStatus,
    }
  })

export type CurrentUser = NonNullable<Awaited<ReturnType<typeof getCurrentUser>>>
