import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchVulnerabilities } from '@/api/vulnerabilities.functions'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'

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
  loader: ({ deps }) =>
    fetchVulnerabilities({
      data: {
        ...(deps.search ? { search: deps.search } : {}),
        ...(deps.severity ? { severity: deps.severity } : {}),
        ...(deps.status ? { status: deps.status } : {}),
        ...(deps.source ? { source: deps.source } : {}),
        ...(deps.presentOnly ? { presentOnly: true } : { presentOnly: false }),
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
    queryKey: ['vulnerabilities', search.search, search.severity, search.status, search.source, search.presentOnly, search.recurrenceOnly, search.page, search.pageSize],
    queryFn: () =>
      fetchVulnerabilities({
        data: {
          ...(search.search ? { search: search.search } : {}),
          ...(search.severity ? { severity: search.severity } : {}),
          ...(search.status ? { status: search.status } : {}),
          ...(search.source ? { source: search.source } : {}),
          ...(search.presentOnly ? { presentOnly: true } : { presentOnly: false }),
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
        searchValue={search.search}
        severityFilter={search.severity}
        statusFilter={search.status}
        sourceFilter={search.source}
        presentOnly={search.presentOnly}
        recurrenceOnly={search.recurrenceOnly}
        onSearchChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, search: value, page: 1 }),
          })
        }}
        onSeverityFilterChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, severity: value, page: 1 }),
          })
        }}
        onStatusFilterChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, status: value, page: 1 }),
          })
        }}
        onSourceFilterChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, source: value, page: 1 }),
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
        onRecurrenceOnlyChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, recurrenceOnly: value, page: 1 }),
          })
        }}
        onPresentOnlyChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, presentOnly: value, page: 1 }),
          })
        }}
        onClearFilters={() => {
          void navigate({
            search: (prev) => ({
              ...prev,
              search: '',
              severity: '',
              status: '',
              source: '',
              recurrenceOnly: false,
              presentOnly: true,
              page: 1,
            }),
          })
        }}
      />
    </section>
  )
}
