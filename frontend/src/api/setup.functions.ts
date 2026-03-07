import { createServerFn } from '@tanstack/react-start'
import { apiGet, apiPost } from '@/server/api'
import { setupStatusSchema, setupPayloadSchema } from './setup.schemas'

export const fetchSetupStatus = createServerFn({ method: 'GET' })
  .handler(async () => {
    // Setup does not require auth — it runs before any tenant exists
    const data = await apiGet('/setup/status', '')
    return setupStatusSchema.parse(data)
  })

export const completeSetup = createServerFn({ method: 'POST' })
  .inputValidator(setupPayloadSchema)
  .handler(async ({ data: payload }) => {
    await apiPost('/setup/complete', '', payload)
  })
