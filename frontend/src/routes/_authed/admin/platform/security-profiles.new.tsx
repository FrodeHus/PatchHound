import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { toast } from 'sonner'
import { createSecurityProfile } from '@/api/security-profiles.functions'
import { SecurityProfileWorkbench } from '@/components/features/admin/security-profiles/SecurityProfileWorkbench'
import {
  createSecurityProfileDraft,
  securityProfilePayload,
  type SecurityProfileDraft,
} from '@/components/features/admin/security-profiles/security-profile-workbench-model'
import { useTenantScope } from '@/components/layout/tenant-scope'

export const Route = createFileRoute('/_authed/admin/platform/security-profiles/new')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  component: NewSecurityProfilePage,
})

function NewSecurityProfilePage() {
  const navigate = Route.useNavigate()
  const queryClient = useQueryClient()
  const { selectedTenantId, tenants } = useTenantScope()
  const [draft, setDraft] = useState<SecurityProfileDraft>(() => createSecurityProfileDraft())
  const tenantName = tenants.find((tenant) => tenant.id === selectedTenantId)?.name ?? 'No tenant selected'

  const saveMutation = useMutation({
    mutationFn: async () => {
      await createSecurityProfile({ data: securityProfilePayload(draft) })
    },
    onSuccess: async () => {
      toast.success('Security profile created')
      await queryClient.invalidateQueries({ queryKey: ['security-profiles'] })
      void navigate({ to: '/admin/platform/security-profiles', search: { page: 1, pageSize: 25 } })
    },
    onError: () => {
      toast.error('Failed to create security profile')
    },
  })

  return (
    <SecurityProfileWorkbench
      mode="create"
      tenantName={tenantName}
      draft={draft}
      isSaving={saveMutation.isPending}
      onDraftChange={setDraft}
      onCancel={() => void navigate({ to: '/admin/platform/security-profiles', search: { page: 1, pageSize: 25 } })}
      onSave={() => saveMutation.mutate()}
    />
  )
}
