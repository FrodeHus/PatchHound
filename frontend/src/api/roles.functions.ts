import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { getSession } from '@/server/session'
import { apiPost } from '@/server/api'

type ActivateRolesResponse = {
  roles: string[]
}

export const activateRoles = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator((data: { roles: string[] }) => data)
  .handler(async ({ context, data }) => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      throw new Error('Not authenticated')
    }

    const response = await apiPost<ActivateRolesResponse>(
      '/roles/activate',
      {
        token: context.token,
        tenantId: context.tenantId,
        activeRoles: context.activeRoles ?? [],
      },
      { roles: data.roles },
    )

    session.activeRoles = response.roles
    await session.save()

    return { activeRoles: response.roles }
  })

export const clearActiveRoles = createServerFn({ method: 'POST' })
  .handler(async () => {
    const session = await getSession()
    session.activeRoles = []
    await session.save()
    return { activeRoles: [] }
  })
