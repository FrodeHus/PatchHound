import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchTenantSoftware } from '@/api/software.functions'
import { SoftwareRiskDetailDialog } from '@/components/features/software/SoftwareRiskDetailDialog'
import { SoftwareTable } from '@/components/features/software/SoftwareTable'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { buildSoftwareListRequest, softwareQueryKeys } from '@/features/software/list-state'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/list-search-helpers'

const softwareSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  confidence: searchStringSchema,
  vulnerableOnly: searchBooleanSchema,
  boundOnly: searchBooleanSchema,
})

export const Route = createFileRoute('/_authed/software/')({
  validateSearch: softwareSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchTenantSoftware({ data: buildSoftwareListRequest(deps) }),
  component: SoftwareIndexPage,
})

function SoftwareIndexPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const [selectedRiskSoftwareId, setSelectedRiskSoftwareId] = useState<string | null>(null)
  const canUseInitialData = initialTenantId === selectedTenantId
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const query = useQuery({
    queryKey: softwareQueryKeys.list(selectedTenantId, search),
    queryFn: () => fetchTenantSoftware({ data: buildSoftwareListRequest(search) }),
    initialData: canUseInitialData ? initialData : undefined,
  })
  const data = query.data ?? (canUseInitialData ? initialData : undefined)

  if (!data) {
    return null
  }

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Software</h1>
      <SoftwareTable
        items={data.items}
        totalCount={data.totalCount}
        page={data.page}
        pageSize={data.pageSize}
        totalPages={data.totalPages}
        searchValue={search.search}
        confidenceFilter={search.confidence}
        vulnerableOnly={search.vulnerableOnly}
        boundOnly={search.boundOnly}
        onSearchChange={(value) => {
          searchActions.updateField('search', value)
        }}
        onConfidenceFilterChange={(value) => {
          searchActions.updateField('confidence', value)
        }}
        onVulnerableOnlyChange={(value) => {
          searchActions.updateField('vulnerableOnly', value)
        }}
        onBoundOnlyChange={(value) => {
          searchActions.updateField('boundOnly', value)
        }}
        onApplyStructuredFilters={(filters) => {
          searchActions.updateFields({
            confidence: filters.confidence,
            vulnerableOnly: filters.vulnerableOnly,
            boundOnly: filters.boundOnly,
          })
        }}
        onShowRiskDetail={setSelectedRiskSoftwareId}
        onPageChange={(page) => {
          searchActions.updatePage(page)
        }}
        onPageSizeChange={(pageSize) => {
          searchActions.updatePageSize(pageSize)
        }}
        onClearFilters={() => {
          searchActions.updateFields({
            search: '',
            confidence: '',
            vulnerableOnly: false,
            boundOnly: false,
          })
        }}
      />
      <SoftwareRiskDetailDialog
        tenantSoftwareId={selectedRiskSoftwareId}
        open={selectedRiskSoftwareId !== null}
        onOpenChange={(open) => {
          if (!open) {
            setSelectedRiskSoftwareId(null)
          }
        }}
      />
    </section>
  )
}
