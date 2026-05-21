import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchDecisionList } from '@/api/remediation.functions'
import { fetchVulnerabilities } from '@/api/vulnerabilities.functions'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { buildVulnerabilitiesListRequest, vulnerabilityQueryKeys } from '@/features/vulnerabilities/list-state'
import { vulnerabilityStatusOptions } from '@/lib/options/vulnerabilities'
import { baseListSearchSchema, searchBooleanSchema, searchBooleanTrueSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/-list-search-helpers'

const vulnerabilitiesSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  severity: searchStringSchema,
  status: searchStringSchema,
  source: searchStringSchema,
  ageOperator: searchStringSchema,
  ageHours: searchStringSchema,
  publicExploitOnly: searchBooleanSchema,
  knownExploitedOnly: searchBooleanSchema,
  activeAlertOnly: searchBooleanSchema,
  presentOnly: searchBooleanTrueSchema,
  hasAssessmentOnly: searchBooleanSchema,
  remediationCaseIds: searchStringSchema.optional(),
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
  const remediationOptionsQuery = useQuery({
    queryKey: ['vulnerabilities', 'remediation-filter-options', selectedTenantId],
    queryFn: () => fetchDecisionList({
      data: {
        approvalStatus: 'Approved',
        page: 1,
        pageSize: 100,
      },
    }),
    enabled: Boolean(selectedTenantId),
    staleTime: 60_000,
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
        ageOperator={search.ageOperator}
        ageHours={search.ageHours}
        publicExploitOnly={search.publicExploitOnly}
        knownExploitedOnly={search.knownExploitedOnly}
        activeAlertOnly={search.activeAlertOnly}
        presentOnly={search.presentOnly}
        hasAssessmentOnly={search.hasAssessmentOnly}
        selectedRemediationCaseIds={parseRemediationCaseIds(search.remediationCaseIds ?? '')}
        remediationOptions={(remediationOptionsQuery.data?.items ?? []).map((item) => ({
          remediationCaseId: item.remediationCaseId,
          softwareName: item.softwareName,
          outcome: item.outcome,
        }))}
        onSearchChange={(value) => {
          searchActions.updateField('search', value)
        }}
        onSeverityFilterChange={(value) => {
          searchActions.updateField('severity', value)
        }}
        onStatusFilterChange={(value) => {
          searchActions.updateField('status', value)
        }}
        onPageChange={(page) => {
          searchActions.updatePage(page)
        }}
        onPageSizeChange={(nextPageSize) => {
          searchActions.updatePageSize(nextPageSize)
        }}
        onAgeFilterChange={(operator, hours) => {
          searchActions.updateFields({ ageOperator: operator, ageHours: hours })
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
        onPresentOnlyChange={(value) => {
          searchActions.updateField('presentOnly', value)
        }}
        onHasAssessmentOnlyChange={(value) => {
          searchActions.updateField('hasAssessmentOnly', value)
        }}
        onRemediationCaseIdsChange={(value) => {
          searchActions.updateField('remediationCaseIds', value.join(','))
        }}
        onApplyStructuredFilters={(filters) => {
          searchActions.updateFields({
            severity: filters.severity,
            status: filters.status,
            ageOperator: filters.ageOperator,
            ageHours: filters.ageHours,
            publicExploitOnly: filters.publicExploitOnly,
            knownExploitedOnly: filters.knownExploitedOnly,
            activeAlertOnly: filters.activeAlertOnly,
            presentOnly: filters.presentOnly,
            hasAssessmentOnly: filters.hasAssessmentOnly,
            remediationCaseIds: filters.remediationCaseIds.join(','),
          })
        }}
        onClearFilters={() => {
          searchActions.updateFields({
            search: '',
            severity: '',
            status: vulnerabilityStatusOptions[0],
            source: '',
            ageOperator: '',
            ageHours: '',
            publicExploitOnly: false,
            knownExploitedOnly: false,
            activeAlertOnly: false,
            presentOnly: true,
            hasAssessmentOnly: false,
            remediationCaseIds: '',
          })
        }}
      />
    </section>
  )
}

function parseRemediationCaseIds(value: string) {
  return value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
}
