import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import { getSession } from '@/server/session'
import { resolveTenantDisplayName } from '@/server/auth'
import { setupContextSchema, setupStatusSchema, setupPayloadSchema } from './setup.schemas'

export const fetchSetupStatus = createServerFn({ method: 'GET' })
  .handler(async () => {
    // Setup does not require auth — it runs before any tenant exists
    const data = await apiGet('/setup/status', '')
    return setupStatusSchema.parse(data)
  })

export const fetchSetupContext = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async () => {
    const session = await getSession()

    if (!session.tenantId || !session.userId || !session.email) {
      throw new Error('Authenticated setup context is incomplete')
    }

    if (!session.entraRoles?.includes('Tenant.Admin')) {
      throw new Error(
        'You are signed in, but your account is missing the required Tenant.Admin Entra role. Ask an administrator to assign Tenant.Admin, then sign in again to continue setup.',
      )
    }

    const tenantName = session.tenantName
      ?? await resolveTenantDisplayName(session.tenantId).catch(() => session.tenantId)

    return setupContextSchema.parse({
      tenantName,
      entraTenantId: session.tenantId,
      adminEmail: session.email,
      adminDisplayName: session.displayName ?? session.email,
      adminEntraObjectId: session.userId,
    })
  })

export const completeSetup = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(setupPayloadSchema)
  .handler(async ({ context }) => {
    const status = await apiGet('/setup/status', '')
    const { isInitialized } = setupStatusSchema.parse(status)
    if (isInitialized) {
      throw new Error('Setup has already been completed')
    }

    const setupContext = await fetchSetupContext()
    await apiPost('/setup/complete', context.token, {
      tenantName: setupContext.tenantName,
    })
  })
