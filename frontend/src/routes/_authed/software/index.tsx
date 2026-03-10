import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchNormalizedSoftware } from '@/api/software.functions'
import { SoftwareTable } from '@/components/features/software/SoftwareTable'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'

const softwareSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  confidence: searchStringSchema,
  vulnerableOnly: searchBooleanSchema,
  boundOnly: searchBooleanSchema,
})

export const Route = createFileRoute('/_authed/software/')({
  validateSearch: softwareSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchNormalizedSoftware({
      data: {
        ...(deps.search ? { search: deps.search } : {}),
        ...(deps.confidence ? { confidence: deps.confidence } : {}),
        ...(deps.vulnerableOnly ? { vulnerableOnly: true } : {}),
        ...(deps.boundOnly ? { boundOnly: true } : {}),
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: SoftwareIndexPage,
})

function SoftwareIndexPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const query = useQuery({
    queryKey: ['normalized-software-list', search.search, search.confidence, search.vulnerableOnly, search.boundOnly, search.page, search.pageSize],
    queryFn: () =>
      fetchNormalizedSoftware({
        data: {
          ...(search.search ? { search: search.search } : {}),
          ...(search.confidence ? { confidence: search.confidence } : {}),
          ...(search.vulnerableOnly ? { vulnerableOnly: true } : {}),
          ...(search.boundOnly ? { boundOnly: true } : {}),
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
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
          void navigate({ search: (prev) => ({ ...prev, search: value, page: 1 }) })
        }}
        onConfidenceFilterChange={(value) => {
          void navigate({ search: (prev) => ({ ...prev, confidence: value, page: 1 }) })
        }}
        onVulnerableOnlyChange={(value) => {
          void navigate({ search: (prev) => ({ ...prev, vulnerableOnly: value, page: 1 }) })
        }}
        onBoundOnlyChange={(value) => {
          void navigate({ search: (prev) => ({ ...prev, boundOnly: value, page: 1 }) })
        }}
        onPageChange={(page) => {
          void navigate({ search: (prev) => ({ ...prev, page }) })
        }}
        onPageSizeChange={(pageSize) => {
          void navigate({ search: (prev) => ({ ...prev, pageSize, page: 1 }) })
        }}
        onClearFilters={() => {
          void navigate({
            search: (prev) => ({
              ...prev,
              search: '',
              confidence: '',
              vulnerableOnly: false,
              boundOnly: false,
              page: 1,
            }),
          })
        }}
      />
    </section>
  )
}
