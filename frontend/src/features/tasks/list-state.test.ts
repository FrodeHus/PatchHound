import { describe, expect, it } from 'vitest'
import { buildTasksListRequest, taskQueryKeys } from '@/features/tasks/list-state'

describe('buildTasksListRequest', () => {
  it('omits an empty status while preserving paging', () => {
    expect(
      buildTasksListRequest({
        status: '',
        page: 2,
        pageSize: 50,
      }),
    ).toEqual({
      page: 2,
      pageSize: 50,
    })
  })

  it('includes the selected status when present', () => {
    expect(
      buildTasksListRequest({
        status: 'InProgress',
        page: 1,
        pageSize: 25,
      }),
    ).toEqual({
      status: 'InProgress',
      page: 1,
      pageSize: 25,
    })
  })
})

describe('taskQueryKeys', () => {
  it('builds a stable list key', () => {
    expect(
      taskQueryKeys.list({
        status: 'Completed',
        page: 4,
        pageSize: 100,
      }),
    ).toEqual(['tasks', 'list', 'Completed', 4, 100])
  })
})
