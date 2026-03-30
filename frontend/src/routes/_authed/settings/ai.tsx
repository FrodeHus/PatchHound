import { createFileRoute, redirect } from '@tanstack/react-router'
import { TenantAiSettingsPage } from '@/components/features/settings/TenantAiSettingsPage'
import { z } from 'zod'

export const Route = createFileRoute('/_authed/settings/ai')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/dashboard' })
    }
  },
  validateSearch: z.object({
    mode: z.enum(['new', 'edit']).optional(),
    profileId: z.string().optional(),
  }),
  component: TenantAiSettingsPage,
})
