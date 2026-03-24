import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import {
  fetchApprovalTasks,
  markApprovalTaskRead,
} from '@/api/approval-tasks.functions'
import { ApprovalInbox } from '@/components/features/approvals/ApprovalInbox'
import { useTenantScope } from '@/components/layout/tenant-scope'
import {
  baseListSearchSchema,
  searchStringSchema,
  searchBooleanSchema,
} from '@/routes/-list-search'

const approvalSearchSchema = baseListSearchSchema.extend({
  status: searchStringSchema,
  type: searchStringSchema,
  search: searchStringSchema,
  showRead: searchBooleanSchema,
})

export const Route = createFileRoute('/_authed/approvals/')({
  validateSearch: approvalSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchApprovalTasks({
      data: {
        ...normalizeFilters(deps),
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: ApprovalsRoute,
})

function ApprovalsRoute() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const initialData = Route.useLoaderData()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: ['approval-tasks', selectedTenantId, search],
    queryFn: () =>
      fetchApprovalTasks({
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
    <ApprovalInbox
      data={data}
      filters={{
        status: search.status,
        type: search.type,
        search: search.search,
        showRead: search.showRead,
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
      onMarkRead={async (id) => {
        await markApprovalTaskRead({ data: { id } })
        void queryClient.invalidateQueries({
          queryKey: ['approval-tasks'],
        })
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
