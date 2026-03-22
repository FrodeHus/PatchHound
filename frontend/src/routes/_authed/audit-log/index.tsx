import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { AuditLogTable } from '@/components/features/audit/AuditLogTable'
import { auditQueryKeys, buildAuditLogListRequest } from '@/features/audit/list-state'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/-list-search-helpers'

const auditLogSearchSchema = baseListSearchSchema.extend({
  action: searchStringSchema,
  entityType: searchStringSchema,
})

export const Route = createFileRoute('/_authed/audit-log/')({
  validateSearch: auditLogSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchAuditLog({ data: buildAuditLogListRequest(deps) }),
  component: AuditLogPage,
})

function AuditLogPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const query = useQuery({
    queryKey: auditQueryKeys.list(search),
    queryFn: () => fetchAuditLog({ data: buildAuditLogListRequest(search) }),
    initialData,
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Audit Log</h1>
      <AuditLogTable
        items={query.data.items}
        totalCount={query.data.totalCount}
        page={query.data.page}
        pageSize={query.data.pageSize}
        totalPages={query.data.totalPages}
        actionFilter={search.action}
        entityTypeFilter={search.entityType}
        onActionFilterChange={(action) => {
          searchActions.updateField('action', action)
        }}
        onEntityTypeFilterChange={(entityType) => {
          searchActions.updateField('entityType', entityType)
        }}
        onPageChange={(page) => {
          searchActions.updatePage(page)
        }}
        onPageSizeChange={(nextPageSize) => {
          searchActions.updatePageSize(nextPageSize)
        }}
        onClearFilters={() => {
          searchActions.updateFields({ action: '', entityType: '' })
        }}
      />
    </section>
  )
}
