import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost } from '@/server/api'
import { notificationsSchema } from './notifications.schemas'
import { z } from 'zod'

export const fetchNotifications = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ take: z.number().optional() }))
  .handler(async ({ context, data: { take } }) => {
    const qs = take ? `?take=${take}` : ''
    const data = await apiGet(`/notifications${qs}`, context)
    return notificationsSchema.parse(data)
  })

export const fetchUnreadNotificationCount = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    return await apiGet<number>('/notifications/unread-count', context)
  })

export const markNotificationRead = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiPost(`/notifications/${id}/read`, context)
  })
