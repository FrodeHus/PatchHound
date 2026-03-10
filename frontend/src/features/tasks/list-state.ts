type TasksListSearch = {
  status: string
  page: number
  pageSize: number
}

export function buildTasksListRequest(search: TasksListSearch) {
  return {
    ...(search.status ? { status: search.status } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const taskQueryKeys = {
  all: ['tasks'] as const,
  list: (search: TasksListSearch) => [
    ...taskQueryKeys.all,
    'list',
    search.status,
    search.page,
    search.pageSize,
  ] as const,
}
