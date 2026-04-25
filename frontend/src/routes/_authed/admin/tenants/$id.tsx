import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { z } from 'zod'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { fetchTenantDetail } from '@/api/settings.functions'
import { TenantAdministrationDetail } from '@/components/features/admin/TenantAdministrationDetail'

const tenantDetailSearchSchema = z.object({
  tab: z.enum(['overview', 'sources', 'ai-settings', 'device-rules', 'workflows', 'business-labels']).optional().default('overview'),
  mode: z.enum(['edit', 'history', 'new']).optional(),
  sourceKey: z.string().optional(),
  profileId: z.string().uuid().optional(),
  ruleId: z.string().uuid().optional(),
})

export const Route = createFileRoute('/_authed/admin/tenants/$id')({
  validateSearch: tenantDetailSearchSchema,
  loader: ({ params }) => fetchTenantDetail({ data: { tenantId: params.id } }),
  component: TenantDetailPage,
})

function TenantDetailPage() {
  const { user } = Route.useRouteContext()
  const tenant = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
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
    enabled: canViewAudit && search.tab === 'overview',
    staleTime: 30_000,
  })

  return (
    <TenantAdministrationDetail
      key={tenant.id}
      tenant={tenant}
      canViewAudit={canViewAudit}
      recentAuditItems={canViewAudit ? (recentAuditQuery.data?.items ?? []) : []}
      activeTab={search.tab}
      tabSearch={{ mode: search.mode, sourceKey: search.sourceKey, profileId: search.profileId, ruleId: search.ruleId }}
      onTabChange={(tab) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            tab,
            mode: undefined,
            sourceKey: undefined,
            profileId: undefined,
            ruleId: undefined,
          }),
        })
      }}
      onSearchChange={(patch) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            mode: patch.mode as 'edit' | 'history' | 'new' | undefined,
            sourceKey: patch.sourceKey,
            profileId: patch.profileId,
            ruleId: patch.ruleId,
          }),
        })
      }}
    />
  )
}
