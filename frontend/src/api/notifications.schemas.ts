import { z } from 'zod'

export const notificationSchema = z.object({
  id: z.string().uuid(),
  type: z.string(),
  title: z.string(),
  body: z.string(),
  sentAt: z.string().datetime({ offset: true }),
  readAt: z.string().datetime({ offset: true }).nullable(),
  relatedEntityType: z.string().nullable(),
  relatedEntityId: z.string().uuid().nullable(),
  path: z.string().nullable(),
})

export const notificationsSchema = z.array(notificationSchema)

export type AppNotification = z.infer<typeof notificationSchema>
