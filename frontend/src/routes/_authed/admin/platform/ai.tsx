import { createFileRoute, redirect } from '@tanstack/react-router'
import { TenantAiSettingsPage } from '@/components/features/settings/TenantAiSettingsPage'
import { z } from 'zod'

export const Route = createFileRoute('/_authed/admin/platform/ai')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: z.object({
    mode: z.enum(['new', 'edit']).optional(),
    profileId: z.string().optional(),
  }),
  component: AiSettingsRoute,
})

function AiSettingsRoute() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()

  return (
    <TenantAiSettingsPage
      mode={search.mode ?? null}
      profileId={search.profileId ?? null}
      onSearchChange={(patch) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            mode: patch.mode as 'edit' | 'new' | undefined,
            profileId: patch.profileId,
          }),
        })
      }}
      onClearSearch={() => {
        void navigate({ search: {} })
      }}
    />
  )
}
