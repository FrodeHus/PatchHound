import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchMyTasks } from '@/api/my-tasks.functions'
import { MyTasksPage } from '@/components/features/tasks/MyTasksPage'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { baseListSearchSchema } from '@/routes/-list-search'

const myTasksSearchSchema = baseListSearchSchema.extend({
  recommendationPage: baseListSearchSchema.shape.page,
  decisionPage: baseListSearchSchema.shape.page,
  approvalPage: baseListSearchSchema.shape.page,
})

export const Route = createFileRoute('/_authed/my-tasks')({
  validateSearch: myTasksSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchMyTasks({ data: toMyTasksQuery(deps) }),
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
    queryFn: () => fetchMyTasks({ data: toMyTasksQuery(search) }),
    initialData: canUseInitialData ? initialData : undefined,
  })

  const data = query.data ?? (canUseInitialData ? initialData : undefined)
  if (!data) {
    return null
  }

  return (
    <MyTasksPage
      sections={data.sections}
      pageSize={search.pageSize}
      onLoadNext={(bucket) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            [`${bucket}Page`]: prev[`${bucket}Page`] + 1,
          }),
        })
      }}
      onPageSizeChange={(pageSize) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            pageSize,
            recommendationPage: 1,
            decisionPage: 1,
            approvalPage: 1,
          }),
        })
      }}
    />
  )
}

function toMyTasksQuery(search: {
  pageSize: number
  recommendationPage: number
  decisionPage: number
  approvalPage: number
}) {
  return {
    pageSize: search.pageSize,
    recommendationPage: search.recommendationPage,
    decisionPage: search.decisionPage,
    approvalPage: search.approvalPage,
  }
}
