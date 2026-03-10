import { describe, expect, it, vi } from 'vitest'
import { createListSearchUpdater } from '@/routes/list-search-helpers'

type TestSearch = {
  search: string
  status: string
  page: number
  pageSize: number
}

describe('createListSearchUpdater', () => {
  const initialSearch: TestSearch = {
    search: 'kernel',
    status: 'open',
    page: 3,
    pageSize: 25,
  }

  it('resets page when updating a single field', () => {
    const navigate = vi.fn()
    const updater = createListSearchUpdater<TestSearch>(navigate)

    updater.updateField('status', 'resolved')

    expect(navigate).toHaveBeenCalledTimes(1)
    const options = navigate.mock.calls[0][0] as { search: (prev: TestSearch) => TestSearch }
    expect(options.search(initialSearch)).toEqual({
      ...initialSearch,
      status: 'resolved',
      page: 1,
    })
  })

  it('supports multi-field updates without resetting page when requested', () => {
    const navigate = vi.fn()
    const updater = createListSearchUpdater<TestSearch>(navigate)

    updater.updateFields({ search: 'openssl', status: '' }, { resetPage: false })

    const options = navigate.mock.calls[0][0] as { search: (prev: TestSearch) => TestSearch }
    expect(options.search(initialSearch)).toEqual({
      ...initialSearch,
      search: 'openssl',
      status: '',
    })
  })

  it('resets page when page size changes', () => {
    const navigate = vi.fn()
    const updater = createListSearchUpdater<TestSearch>(navigate)

    updater.updatePageSize(50)

    const options = navigate.mock.calls[0][0] as { search: (prev: TestSearch) => TestSearch }
    expect(options.search(initialSearch)).toEqual({
      ...initialSearch,
      page: 1,
      pageSize: 50,
    })
  })
})
