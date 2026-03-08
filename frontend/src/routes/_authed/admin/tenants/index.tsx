import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useNavigate, useRouter } from '@tanstack/react-router'
import { createTenant, fetchTenants } from '@/api/settings.functions'
import { TenantAdministrationList } from '@/components/features/admin/TenantAdministrationList'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/tenants/')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchTenants({ data: { page: deps.page, pageSize: deps.pageSize } }),
  component: TenantAdministrationPage,
})

function TenantAdministrationPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const routeNavigate = Route.useNavigate()
  const navigate = useNavigate()
  const router = useRouter()
  const query = useQuery({
    queryKey: ['tenants', search.page, search.pageSize],
    queryFn: () => fetchTenants({ data: { page: search.page, pageSize: search.pageSize } }),
    initialData,
  })
  const createMutation = useMutation({
    mutationFn: createTenant,
    onSuccess: async (tenant) => {
      await router.invalidate()
      await navigate({ to: '/admin/tenants/$id', params: { id: tenant.id } })
    },
  })

  return (
    <TenantAdministrationList
      tenants={query.data.items}
      totalCount={query.data.totalCount}
      page={query.data.page}
      pageSize={query.data.pageSize}
      totalPages={query.data.totalPages}
      isCreating={createMutation.isPending}
      createError={createMutation.error instanceof Error ? createMutation.error.message : null}
      onPageChange={(page) => {
        void routeNavigate({
          search: (prev) => ({ ...prev, page }),
        })
      }}
      onPageSizeChange={(nextPageSize) => {
        void routeNavigate({
          search: (prev) => ({ ...prev, pageSize: nextPageSize, page: 1 }),
        })
      }}
      onCreate={(payload) => createMutation.mutateAsync({ data: payload })}
    />
  )
}
