import { createServerFn } from '@tanstack/react-start'
import { getSession } from '@/server/session'
import { apiPost } from '@/server/api'

type ActivateRolesResponse = {
  roles: string[]
}

export const activateRoles = createServerFn({ method: 'POST' })
  .inputValidator((data: { roles: string[] }) => data)
  .handler(async ({ data }) => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      throw new Error('Not authenticated')
    }

    const response = await apiPost<ActivateRolesResponse>(
      '/roles/activate',
      {
        token: session.accessToken,
        tenantId: session.tenantId,
        activeRoles: session.activeRoles ?? [],
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
