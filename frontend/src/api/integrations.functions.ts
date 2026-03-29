import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import {
  sentinelConnectorSchema,
  updateSentinelConnectorSchema,
} from './integrations.schemas'

export const fetchSentinelConnector = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/integrations/sentinel-connector', context)
    return sentinelConnectorSchema.parse(data)
  })

export const updateSentinelConnector = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(updateSentinelConnectorSchema)
  .handler(async ({ context, data }) => {
    await apiPut('/integrations/sentinel-connector', context, data)
  })
