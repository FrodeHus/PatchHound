import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { AuditLogTable } from '@/components/features/audit/AuditLogTable'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/audit-log/')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchAuditLog({ data: { page: deps.page, pageSize: deps.pageSize } }),
  component: AuditLogPage,
})

function AuditLogPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const query = useQuery({
    queryKey: ['audit-log', search.page, search.pageSize],
    queryFn: () => fetchAuditLog({ data: { page: search.page, pageSize: search.pageSize } }),
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
      />
    </section>
  )
}
