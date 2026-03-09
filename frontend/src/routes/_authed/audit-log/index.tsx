import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { AuditLogTable } from '@/components/features/audit/AuditLogTable'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'

const auditLogSearchSchema = baseListSearchSchema.extend({
  action: searchStringSchema,
  entityType: searchStringSchema,
})

export const Route = createFileRoute('/_authed/audit-log/')({
  validateSearch: auditLogSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchAuditLog({
      data: {
        ...(deps.action ? { action: deps.action } : {}),
        ...(deps.entityType ? { entityType: deps.entityType } : {}),
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: AuditLogPage,
})

function AuditLogPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const query = useQuery({
    queryKey: ['audit-log', search.action, search.entityType, search.page, search.pageSize],
    queryFn: () =>
      fetchAuditLog({
        data: {
          ...(search.action ? { action: search.action } : {}),
          ...(search.entityType ? { entityType: search.entityType } : {}),
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
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
          void navigate({
            search: (prev) => ({ ...prev, action, page: 1 }),
          })
        }}
        onEntityTypeFilterChange={(entityType) => {
          void navigate({
            search: (prev) => ({ ...prev, entityType, page: 1 }),
          })
        }}
        onPageChange={(page) => {
          void navigate({
            search: (prev) => ({ ...prev, page }),
          })
        }}
        onPageSizeChange={(nextPageSize) => {
          void navigate({
            search: (prev) => ({ ...prev, pageSize: nextPageSize, page: 1 }),
          })
        }}
        onClearFilters={() => {
          void navigate({
            search: (prev) => ({ ...prev, action: '', entityType: '', page: 1 }),
          })
        }}
      />
    </section>
  )
}
