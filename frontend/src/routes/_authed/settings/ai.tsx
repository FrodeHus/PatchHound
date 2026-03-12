import { createFileRoute } from '@tanstack/react-router'
import { TenantAiSettingsPage } from '@/components/features/settings/TenantAiSettingsPage'
import { z } from 'zod'

export const Route = createFileRoute('/_authed/settings/ai')({
  validateSearch: z.object({
    mode: z.enum(['new', 'edit']).optional(),
    profileId: z.string().optional(),
  }),
  component: TenantAiSettingsPage,
})
