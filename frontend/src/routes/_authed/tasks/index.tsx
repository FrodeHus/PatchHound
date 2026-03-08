import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { fetchTasks } from '@/api/tasks.functions'
import { updateTaskStatus } from '@/api/tasks.functions'
import { TaskList } from '@/components/features/tasks/TaskList'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/tasks/')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchTasks({ data: { page: deps.page, pageSize: deps.pageSize } }),
  component: TasksPage,
})

function TasksPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const router = useRouter()
  const query = useQuery({
    queryKey: ['tasks', search.page, search.pageSize],
    queryFn: () => fetchTasks({ data: { page: search.page, pageSize: search.pageSize } }),
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
        isUpdating={mutation.isPending}
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
        onUpdateStatus={(taskId, status, justification) => {
          mutation.mutate({ taskId, status, justification })
        }}
      />
    </section>
  )
}
