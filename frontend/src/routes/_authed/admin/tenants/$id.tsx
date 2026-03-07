import { useEffect } from 'react'
import { useMutation } from '@tanstack/react-query'
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
  const canViewAudit = user.roles.includes('GlobalAdmin') || user.roles.includes('Auditor')
  const recentAuditQuery = useMutation({
    mutationFn: async () =>
      fetchAuditLog({
        data: {
          tenantId: tenant.id,
          page: 1,
          pageSize: 5,
        },
      }),
  })

  useEffect(() => {
    if (!canViewAudit || recentAuditQuery.data || recentAuditQuery.isPending) {
      return
    }

    void recentAuditQuery.mutateAsync()
  }, [canViewAudit, recentAuditQuery.data, recentAuditQuery.isPending, recentAuditQuery.mutateAsync])

  return (
    <TenantAdministrationDetail
      key={tenant.id}
      tenant={tenant}
      canViewAudit={canViewAudit}
      recentAuditItems={canViewAudit ? (recentAuditQuery.data?.items ?? []) : []}
    />
  )
}
