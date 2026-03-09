import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { fetchTasks } from '@/api/tasks.functions'
import { updateTaskStatus } from '@/api/tasks.functions'
import { TaskList } from '@/components/features/tasks/TaskList'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'

const tasksSearchSchema = baseListSearchSchema.extend({
  status: searchStringSchema,
})

export const Route = createFileRoute('/_authed/tasks/')({
  validateSearch: tasksSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchTasks({
      data: {
        ...(deps.status ? { status: deps.status } : {}),
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: TasksPage,
})

function TasksPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const router = useRouter()
  const query = useQuery({
    queryKey: ['tasks', search.status, search.page, search.pageSize],
    queryFn: () =>
      fetchTasks({
        data: {
          ...(search.status ? { status: search.status } : {}),
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
    initialData,
  })
  const mutation = useMutation({
    mutationFn: async (payload: { taskId: string; status: string; justification?: string }) => {
      await updateTaskStatus({
        data: {
          id: payload.taskId,
          status: payload.status,
          justification: payload.justification,
        },
      })
    },
    onSuccess: () => { void router.invalidate() },
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Remediation Tasks</h1>
      <TaskList
        tasks={query.data.items}
        totalCount={query.data.totalCount}
        page={query.data.page}
        pageSize={query.data.pageSize}
        totalPages={query.data.totalPages}
        statusFilter={search.status}
        isUpdating={mutation.isPending}
        onStatusFilterChange={(value) => {
          void navigate({
            search: (prev) => ({ ...prev, status: value, page: 1 }),
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
        onClearFilters={() => {
          void navigate({
            search: (prev) => ({ ...prev, status: '', page: 1 }),
          })
        }}
        onUpdateStatus={(taskId, status, justification) => {
          mutation.mutate({ taskId, status, justification })
        }}
      />
    </section>
  )
}
