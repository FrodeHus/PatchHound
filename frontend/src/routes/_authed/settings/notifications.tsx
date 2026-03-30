import { createFileRoute, redirect } from '@tanstack/react-router'
import { NotificationDeliverySettingsPage } from '@/components/features/settings/NotificationDeliverySettingsPage'

export const Route = createFileRoute('/_authed/settings/notifications')({
  beforeLoad: ({ context }) => {
    if (!(context.user?.activeRoles ?? []).includes('GlobalAdmin')) {
      throw redirect({ to: '/dashboard' })
    }
  },
  component: NotificationDeliverySettingsPage,
})
