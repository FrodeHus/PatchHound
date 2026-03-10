import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchVulnerabilities } from '@/api/vulnerabilities.functions'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'
import { buildVulnerabilitiesListRequest, vulnerabilityQueryKeys } from '@/features/vulnerabilities/list-state'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/list-search-helpers'

const vulnerabilitiesSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  severity: searchStringSchema,
  status: searchStringSchema,
  source: searchStringSchema,
  recurrenceOnly: searchBooleanSchema,
  presentOnly: searchBooleanSchema.catch(true),
})

export const Route = createFileRoute('/_authed/vulnerabilities/')({
  validateSearch: vulnerabilitiesSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchVulnerabilities({ data: buildVulnerabilitiesListRequest(deps) }),
  component: VulnerabilitiesPage,
})

function VulnerabilitiesPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const query = useQuery({
    queryKey: vulnerabilityQueryKeys.list(selectedTenantId, search),
    queryFn: () => fetchVulnerabilities({ data: buildVulnerabilitiesListRequest(search) }),
    initialData,
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Vulnerabilities</h1>
      <VulnerabilityTable
        items={query.data.items}
        totalCount={query.data.totalCount}
        page={query.data.page}
        pageSize={query.data.pageSize}
        totalPages={query.data.totalPages}
        searchValue={search.search}
        severityFilter={search.severity}
        statusFilter={search.status}
        sourceFilter={search.source}
        presentOnly={search.presentOnly}
        recurrenceOnly={search.recurrenceOnly}
        onSearchChange={(value) => {
          searchActions.updateField('search', value)
        }}
        onSeverityFilterChange={(value) => {
          searchActions.updateField('severity', value)
        }}
        onStatusFilterChange={(value) => {
          searchActions.updateField('status', value)
        }}
        onSourceFilterChange={(value) => {
          searchActions.updateField('source', value)
        }}
        onPageChange={(page) => {
          searchActions.updatePage(page)
        }}
        onPageSizeChange={(nextPageSize) => {
          searchActions.updatePageSize(nextPageSize)
        }}
        onRecurrenceOnlyChange={(value) => {
          searchActions.updateField('recurrenceOnly', value)
        }}
        onPresentOnlyChange={(value) => {
          searchActions.updateField('presentOnly', value)
        }}
        onClearFilters={() => {
          searchActions.updateFields({
            search: '',
            severity: '',
            status: '',
            source: '',
            recurrenceOnly: false,
            presentOnly: true,
          })
        }}
      />
    </section>
  )
}
