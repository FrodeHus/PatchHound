import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchVulnerabilities } from '@/api/vulnerabilities.functions'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { buildVulnerabilitiesListRequest, vulnerabilityQueryKeys } from '@/features/vulnerabilities/list-state'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/-list-search-helpers'

const vulnerabilitiesSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  severity: searchStringSchema,
  status: searchStringSchema,
  source: searchStringSchema,
  recurrenceOnly: searchBooleanSchema,
  presentOnly: searchBooleanSchema.catch(true),
  minAgeDays: searchStringSchema,
  publicExploitOnly: searchBooleanSchema,
  knownExploitedOnly: searchBooleanSchema,
  activeAlertOnly: searchBooleanSchema,
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
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const query = useQuery({
    queryKey: vulnerabilityQueryKeys.list(selectedTenantId, search),
    queryFn: () => fetchVulnerabilities({ data: buildVulnerabilitiesListRequest(search) }),
    initialData: canUseInitialData ? initialData : undefined,
  })
  const data = query.data ?? (canUseInitialData ? initialData : undefined)

  if (!data) {
    return null
  }

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Vulnerabilities</h1>
      <VulnerabilityTable
        items={data.items}
        totalCount={data.totalCount}
        page={data.page}
        pageSize={data.pageSize}
        totalPages={data.totalPages}
        searchValue={search.search}
        severityFilter={search.severity}
        statusFilter={search.status}
        sourceFilter={search.source}
        presentOnly={search.presentOnly}
        recurrenceOnly={search.recurrenceOnly}
        minAgeDays={search.minAgeDays}
        publicExploitOnly={search.publicExploitOnly}
        knownExploitedOnly={search.knownExploitedOnly}
        activeAlertOnly={search.activeAlertOnly}
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
        onMinAgeDaysChange={(value) => {
          searchActions.updateField('minAgeDays', value)
        }}
        onPublicExploitOnlyChange={(value) => {
          searchActions.updateField('publicExploitOnly', value)
        }}
        onKnownExploitedOnlyChange={(value) => {
          searchActions.updateField('knownExploitedOnly', value)
        }}
        onActiveAlertOnlyChange={(value) => {
          searchActions.updateField('activeAlertOnly', value)
        }}
        onApplyStructuredFilters={(filters) => {
          searchActions.updateFields({
            severity: filters.severity,
            status: filters.status,
            source: filters.source,
            recurrenceOnly: filters.recurrenceOnly,
            presentOnly: filters.presentOnly,
            minAgeDays: filters.minAgeDays,
            publicExploitOnly: filters.publicExploitOnly,
            knownExploitedOnly: filters.knownExploitedOnly,
            activeAlertOnly: filters.activeAlertOnly,
          })
        }}
        onClearFilters={() => {
          searchActions.updateFields({
            search: '',
            severity: '',
            status: '',
            source: '',
            recurrenceOnly: false,
            presentOnly: true,
            minAgeDays: '',
            publicExploitOnly: false,
            knownExploitedOnly: false,
            activeAlertOnly: false,
          })
        }}
      />
    </section>
  )
}
