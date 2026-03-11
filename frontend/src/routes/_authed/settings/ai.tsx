import { createFileRoute } from '@tanstack/react-router'
import { TenantAiSettingsPage } from '@/components/features/settings/TenantAiSettingsPage'

export const Route = createFileRoute('/_authed/settings/ai')({
  component: TenantAiSettingsPage,
})
