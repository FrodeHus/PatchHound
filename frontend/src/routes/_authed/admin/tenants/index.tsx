import { createFileRoute } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { useNavigate, useRouter } from '@tanstack/react-router'
import { createTenant, fetchTenants } from '@/api/settings.functions'
import { TenantAdministrationList } from '@/components/features/admin/TenantAdministrationList'

export const Route = createFileRoute('/_authed/admin/tenants/')({
  loader: () => fetchTenants({ data: {} }),
  component: TenantAdministrationPage,
})

function TenantAdministrationPage() {
  const data = Route.useLoaderData()
  const navigate = useNavigate()
  const router = useRouter()
  const createMutation = useMutation({
    mutationFn: createTenant,
    onSuccess: async (tenant) => {
      await router.invalidate()
      await navigate({ to: '/admin/tenants/$id', params: { id: tenant.id } })
    },
  })

  return (
    <TenantAdministrationList
      tenants={data.items}
      totalCount={data.totalCount}
      isCreating={createMutation.isPending}
      createError={createMutation.error instanceof Error ? createMutation.error.message : null}
      onCreate={(payload) => createMutation.mutateAsync({ data: payload })}
    />
  )
}
