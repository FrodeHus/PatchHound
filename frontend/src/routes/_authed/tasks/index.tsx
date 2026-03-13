import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchTasks } from '@/api/tasks.functions'
import { updateTaskStatus } from '@/api/tasks.functions'
import { TaskList } from '@/components/features/tasks/TaskList'
import { buildTasksListRequest, taskQueryKeys } from '@/features/tasks/list-state'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/list-search-helpers'

const tasksSearchSchema = baseListSearchSchema.extend({
  status: searchStringSchema,
})

export const Route = createFileRoute('/_authed/tasks/')({
  validateSearch: tasksSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchTasks({ data: buildTasksListRequest(deps) }),
  component: TasksPage,
})

function TasksPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: taskQueryKeys.list(search),
    queryFn: () => fetchTasks({ data: buildTasksListRequest(search) }),
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
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: taskQueryKeys.all })
    },
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
          searchActions.updateField('status', value)
        }}
        onApplyStructuredFilters={(filters) => {
          searchActions.updateFields({ status: filters.status })
        }}
        onPageChange={(page) => {
          searchActions.updatePage(page)
        }}
        onPageSizeChange={(nextPageSize) => {
          searchActions.updatePageSize(nextPageSize)
        }}
        onClearFilters={() => {
          searchActions.updateFields({ status: '' })
        }}
        onUpdateStatus={(taskId, status, justification) => {
          mutation.mutate({ taskId, status, justification })
        }}
      />
    </section>
  )
}
