import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { fetchTenantDetail } from '@/api/settings.functions'
import { TenantAdministrationDetail } from '@/components/features/admin/TenantAdministrationDetail'

export const Route = createFileRoute('/_authed/admin/tenants/$id')({
  loader: ({ params }) => fetchTenantDetail({ data: { tenantId: params.id } }),
  component: TenantDetailPage,
})

function TenantDetailPage() {
  const { user } = Route.useRouteContext()
  const tenant = Route.useLoaderData()
  const canViewAudit = (user.activeRoles ?? []).includes('GlobalAdmin') || (user.activeRoles ?? []).includes('Auditor')

  const recentAuditQuery = useQuery({
    queryKey: ['audit-log', 'tenant-detail', tenant.id],
    queryFn: () =>
      fetchAuditLog({
        data: {
          tenantId: tenant.id,
          page: 1,
          pageSize: 5,
        },
      }),
    enabled: canViewAudit,
    staleTime: 30_000,
  })

  return (
    <TenantAdministrationDetail
      key={tenant.id}
      tenant={tenant}
      canViewAudit={canViewAudit}
      recentAuditItems={canViewAudit ? (recentAuditQuery.data?.items ?? []) : []}
    />
  )
}
