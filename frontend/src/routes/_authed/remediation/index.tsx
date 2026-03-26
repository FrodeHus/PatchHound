import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDecisionList } from '@/api/remediation.functions'
import { RemediationWorkbench } from '@/components/features/remediation/RemediationWorkbench'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'

const remediationSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  criticality: searchStringSchema,
  outcome: searchStringSchema,
  approvalStatus: searchStringSchema,
  decisionState: searchStringSchema,
})

export const Route = createFileRoute('/_authed/remediation/')({
  validateSearch: remediationSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchDecisionList({
      data: {
        ...normalizeFilters(deps),
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: RemediationRoute,
})

function RemediationRoute() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const initialData = Route.useLoaderData()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId

  const query = useQuery({
    queryKey: ['decisions', selectedTenantId, search],
    queryFn: () =>
      fetchDecisionList({
        data: {
          ...normalizeFilters(search),
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
    initialData: canUseInitialData ? initialData : undefined,
  })

  const data = query.data ?? (canUseInitialData ? initialData : undefined)
  if (!data) {
    return null
  }

  return (
    <RemediationWorkbench
      data={data}
      filters={{
        search: search.search,
        criticality: search.criticality,
        outcome: search.outcome,
        approvalStatus: search.approvalStatus,
        decisionState: search.decisionState,
      }}
      onFiltersChange={(filters) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            ...filters,
            page: 1,
          }),
        })
      }}
      onPageChange={(page) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            page,
          }),
        })
      }}
    />
  )
}

function normalizeFilters(search: {
  search: string
  criticality: string
  outcome: string
  approvalStatus: string
  decisionState: string
}) {
  return {
    search: search.search || undefined,
    criticality: search.criticality || undefined,
    outcome: search.outcome || undefined,
    approvalStatus: search.approvalStatus || undefined,
    decisionState: search.decisionState || undefined,
  }
}
