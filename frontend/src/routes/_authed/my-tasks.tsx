import { useState } from 'react'
import { useQueries } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDecisionList } from '@/api/remediation.functions'
import { MyTasksPage } from '@/components/features/tasks/MyTasksPage'
import {
  bucketsForRoles,
  BUCKET_FILTERS,
  type TaskBucketKey,
} from '@/components/features/tasks/my-tasks-buckets'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/my-tasks')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  beforeLoad: ({ context }) => {
    const buckets = bucketsForRoles(context.user?.activeRoles ?? [])
    return { buckets }
  },
  loader: async ({ context, deps }) => {
    const buckets = (context as { buckets: TaskBucketKey[] }).buckets
    const results = await Promise.all(
      buckets.map((bucket) =>
        fetchDecisionList({
          data: {
            ...BUCKET_FILTERS[bucket],
            page: deps.page,
            pageSize: deps.pageSize,
          },
        }).then((data) => [bucket, data] as const),
      ),
    )
    return Object.fromEntries(results) as Record<TaskBucketKey, Awaited<ReturnType<typeof fetchDecisionList>>>
  },
  component: MyTasksRoute,
})

function MyTasksRoute() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const initialData = Route.useLoaderData()
  const { buckets } = Route.useRouteContext()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId

  const queries = useQueries({
    queries: buckets.map((bucket) => ({
      queryKey: ['my-tasks', bucket, selectedTenantId, search],
      queryFn: () =>
        fetchDecisionList({
          data: {
            ...BUCKET_FILTERS[bucket],
            page: search.page,
            pageSize: search.pageSize,
          },
        }),
      initialData: canUseInitialData ? initialData[bucket] : undefined,
    })),
  })

  const sections = buckets.flatMap((bucket, index) => {
    const data = queries[index].data
    return data ? [{ bucket, data }] : []
  })

  if (sections.length === 0) {
    return null
  }

  return (
    <MyTasksPage
      sections={sections}
      onPageChange={(page) => {
        void navigate({
          search: (prev) => ({ ...prev, page }),
        })
      }}
    />
  )
}
