import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { toast } from 'sonner'
import { fetchSecurityProfiles, updateSecurityProfile } from '@/api/security-profiles.functions'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import { SecurityProfileWorkbench } from '@/components/features/admin/security-profiles/SecurityProfileWorkbench'
import {
  createSecurityProfileDraft,
  securityProfilePayload,
  type SecurityProfileDraft,
} from '@/components/features/admin/security-profiles/security-profile-workbench-model'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { InsetPanel } from '@/components/ui/inset-panel'

export const Route = createFileRoute('/_authed/admin/platform/security-profiles/$profileId')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  loader: async ({ params }): Promise<SecurityProfile | null> => {
    const profiles = await fetchSecurityProfiles({ data: { page: 1, pageSize: 250 } })
    return profiles.items.find((profile) => profile.id === params.profileId) ?? null
  },
  component: EditSecurityProfilePage,
})

function EditSecurityProfilePage() {
  const loaderData: unknown = Route.useLoaderData()
  const profile = isSecurityProfile(loaderData) ? loaderData : null
  const navigate = Route.useNavigate()
  const queryClient = useQueryClient()
  const { selectedTenantId, tenants } = useTenantScope()
  const tenantName = tenants.find((tenant) => tenant.id === selectedTenantId)?.name ?? 'No tenant selected'
  const [draft, setDraft] = useState<SecurityProfileDraft>(() => createSecurityProfileDraft(profile))

  useEffect(() => {
    setDraft(createSecurityProfileDraft(profile))
  }, [profile])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!profile) {
        throw new Error('Security profile was not found.')
      }

      await updateSecurityProfile({
        data: {
          id: profile.id,
          ...securityProfilePayload(draft),
        },
      })
    },
    onSuccess: async () => {
      toast.success('Security profile updated')
      await queryClient.invalidateQueries({ queryKey: ['security-profiles'] })
      void navigate({ to: '/admin/platform/security-profiles', search: { page: 1, pageSize: 25 } })
    },
    onError: () => {
      toast.error('Failed to update security profile')
    },
  })

  if (!profile) {
    return (
      <InsetPanel className="px-4 py-6 text-sm text-muted-foreground">
        Security profile not found.
      </InsetPanel>
    )
  }

  return (
    <SecurityProfileWorkbench
      mode="edit"
      tenantName={tenantName}
      profile={profile}
      draft={draft}
      isSaving={saveMutation.isPending}
      onDraftChange={setDraft}
      onCancel={() => void navigate({ to: '/admin/platform/security-profiles', search: { page: 1, pageSize: 25 } })}
      onSave={() => saveMutation.mutate()}
    />
  )
}

function isSecurityProfile(value: unknown): value is SecurityProfile {
  return typeof value === 'object'
    && value !== null
    && 'id' in value
    && typeof value.id === 'string'
}
