import { createFileRoute } from '@tanstack/react-router'
import { NotificationDeliverySettingsPage } from '@/components/features/settings/NotificationDeliverySettingsPage'

export const Route = createFileRoute('/_authed/settings/notifications')({
  component: NotificationDeliverySettingsPage,
})
