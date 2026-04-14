import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchRemediationTasks } from '@/api/remediation-tasks.functions'
import { RemediationTaskWorkbench } from '@/components/features/remediation/RemediationTaskWorkbench'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'

const remediationTasksSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  vendor: searchStringSchema,
  criticality: searchStringSchema,
  assetOwner: searchStringSchema,
  taskId: searchStringSchema,
  caseId: searchStringSchema,
  deviceAssetId: searchStringSchema,
})

export const Route = createFileRoute('/_authed/remediation/tasks')({
  validateSearch: remediationTasksSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchRemediationTasks({
      data: {
        ...normalizeFilters(deps),
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: RemediationTasksRoute,
})

function RemediationTasksRoute() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const initialData = Route.useLoaderData()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId

  const query = useQuery({
    queryKey: ['remediation-tasks', selectedTenantId, search],
    queryFn: () =>
      fetchRemediationTasks({
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
    <RemediationTaskWorkbench
      tasks={data}
      filters={{
        search: search.search,
        vendor: search.vendor,
        criticality: search.criticality,
        assetOwner: search.assetOwner,
      }}
      scopedToSoftware={Boolean(search.caseId)}
      scopedToDevice={Boolean(search.deviceAssetId)}
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
  vendor: string
  criticality: string
  assetOwner: string
  caseId: string
  deviceAssetId: string
  taskId: string
}) {
  return {
    search: search.search || undefined,
    vendor: search.vendor || undefined,
    criticality: search.criticality || undefined,
    assetOwner: search.assetOwner || undefined,
    taskId: search.taskId || undefined,
    caseId: search.caseId || undefined,
    deviceAssetId: search.deviceAssetId || undefined,
  }
}
