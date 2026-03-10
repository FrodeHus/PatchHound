import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchNormalizedSoftware } from '@/api/software.functions'
import { SoftwareTable } from '@/components/features/software/SoftwareTable'
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
  loader: ({ deps }) => fetchNormalizedSoftware({ data: buildSoftwareListRequest(deps) }),
  component: SoftwareIndexPage,
})

function SoftwareIndexPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const query = useQuery({
    queryKey: softwareQueryKeys.list(search),
    queryFn: () => fetchNormalizedSoftware({ data: buildSoftwareListRequest(search) }),
    initialData,
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Software</h1>
      <SoftwareTable
        items={query.data.items}
        totalCount={query.data.totalCount}
        page={query.data.page}
        pageSize={query.data.pageSize}
        totalPages={query.data.totalPages}
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
    </section>
  )
}
