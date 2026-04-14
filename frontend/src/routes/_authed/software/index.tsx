import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchTenantSoftware } from '@/api/software.functions'
import { SoftwareRiskDetailDialog } from '@/components/features/software/SoftwareRiskDetailDialog'
import { SoftwareTable } from '@/components/features/software/SoftwareTable'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { buildSoftwareListRequest, softwareQueryKeys } from '@/features/software/list-state'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/-list-search-helpers'

const softwareSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  category: searchStringSchema,
  vulnerableOnly: searchBooleanSchema,
  missedMaintenanceWindow: searchBooleanSchema,
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
        categoryFilter={search.category}
        vulnerableOnly={search.vulnerableOnly}
        missedMaintenanceWindow={search.missedMaintenanceWindow}
        onSearchChange={(value) => {
          searchActions.updateField('search', value)
        }}
        onCategoryFilterChange={(value) => {
          searchActions.updateField('category', value)
        }}
        onVulnerableOnlyChange={(value) => {
          searchActions.updateField('vulnerableOnly', value)
        }}
        onMissedMaintenanceWindowChange={(value) => {
          searchActions.updateField('missedMaintenanceWindow', value)
        }}
        onApplyStructuredFilters={(filters) => {
          searchActions.updateFields({
            category: filters.category,
            vulnerableOnly: filters.vulnerableOnly,
            missedMaintenanceWindow: filters.missedMaintenanceWindow,
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
            category: '',
            vulnerableOnly: false,
            missedMaintenanceWindow: false,
          })
        }}
      />
      <SoftwareRiskDetailDialog
        softwareProductId={selectedRiskSoftwareId}
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
