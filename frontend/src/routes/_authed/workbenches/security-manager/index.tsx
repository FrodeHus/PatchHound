import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import {
  fetchApprovalTasks,
  markApprovalTaskRead,
} from '@/api/approval-tasks.functions'
import { ApprovalWorkbench } from '@/components/features/approvals/ApprovalWorkbench'
import { securityManagerApprovalWorkbenchConfig } from '@/components/features/approvals/approval-workbench-config'
import { useTenantScope } from '@/components/layout/tenant-scope'
import {
  baseListSearchSchema,
  searchBooleanSchema,
  searchStringSchema,
} from '@/routes/-list-search'

const securityManagerWorkbenchSearchSchema = baseListSearchSchema.extend({
  status: searchStringSchema,
  type: searchStringSchema,
  search: searchStringSchema,
  showRead: searchBooleanSchema,
})

export const Route = createFileRoute('/_authed/workbenches/security-manager/')({
  validateSearch: securityManagerWorkbenchSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchApprovalTasks({
      data: {
        ...normalizeFilters(deps),
        status: deps.status || 'Pending',
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: SecurityManagerWorkbenchRoute,
})

function SecurityManagerWorkbenchRoute() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const initialData = Route.useLoaderData()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const queryClient = useQueryClient()
  const filters = {
    status: search.status || 'Pending',
    type: search.type,
    search: search.search,
    showRead: search.showRead,
  }

  const query = useQuery({
    queryKey: ['security-manager-approval-workbench', selectedTenantId, filters, search.page, search.pageSize],
    queryFn: () =>
      fetchApprovalTasks({
        data: {
          ...normalizeFilters(filters),
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
    <ApprovalWorkbench
      config={securityManagerApprovalWorkbenchConfig}
      data={data}
      filters={filters}
      onFiltersChange={(nextFilters) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            ...nextFilters,
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
      onMarkRead={async (id) => {
        await markApprovalTaskRead({ data: { id } })
        void queryClient.invalidateQueries({ queryKey: ['approval-tasks'] })
        void queryClient.invalidateQueries({ queryKey: ['security-manager-approval-workbench'] })
      }}
    />
  )
}

function normalizeFilters(search: {
  status: string
  type: string
  search: string
  showRead: boolean
}) {
  return {
    status: search.status || undefined,
    type: search.type || undefined,
    search: search.search || undefined,
    showRead: search.showRead || undefined,
  }
}
