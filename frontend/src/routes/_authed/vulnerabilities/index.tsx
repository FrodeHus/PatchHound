import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchVulnerabilities } from '@/api/vulnerabilities.functions'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'
import { baseListSearchSchema, searchBooleanSchema } from '@/routes/-list-search'

const vulnerabilitiesSearchSchema = baseListSearchSchema.extend({
  recurrenceOnly: searchBooleanSchema,
})

export const Route = createFileRoute('/_authed/vulnerabilities/')({
  validateSearch: vulnerabilitiesSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchVulnerabilities({
      data: {
        ...(deps.recurrenceOnly ? { recurrenceOnly: true } : {}),
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: VulnerabilitiesPage,
})

function VulnerabilitiesPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const query = useQuery({
    queryKey: ['vulnerabilities', search.recurrenceOnly, search.page, search.pageSize],
    queryFn: () =>
      fetchVulnerabilities({
        data: {
          ...(search.recurrenceOnly ? { recurrenceOnly: true } : {}),
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
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
        recurrenceOnly={search.recurrenceOnly}
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
        onRecurrenceOnlyChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, recurrenceOnly: value, page: 1 }),
          })
        }}
      />
    </section>
  )
}
