import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDecisionList } from '@/api/remediation.functions'
import { MyTasksPage } from '@/components/features/tasks/MyTasksPage'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/my-tasks')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchDecisionList({
      data: {
        needsAnalystRecommendation: true,
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: MyTasksRoute,
})

function MyTasksRoute() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const initialData = Route.useLoaderData()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId

  const query = useQuery({
    queryKey: ['my-tasks', selectedTenantId, search],
    queryFn: () =>
      fetchDecisionList({
        data: {
          needsAnalystRecommendation: true,
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
    <MyTasksPage
      data={data}
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
