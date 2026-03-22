type PagedSearch = {
  page: number
  pageSize: number
}

export function createListSearchUpdater<TSearch extends PagedSearch>(navigate: unknown) {
  const typedNavigate = navigate as (options: {
    search: (prev: TSearch) => TSearch
  }) => Promise<void> | void

  return {
    updateField<TKey extends keyof TSearch>(key: TKey, value: TSearch[TKey]) {
      void typedNavigate({
        search: (prev) => ({
          ...prev,
          [key]: value,
          page: 1,
        }),
      })
    },
    updateFields(updates: Partial<TSearch>, options?: { resetPage?: boolean }) {
      void typedNavigate({
        search: (prev) => ({
          ...prev,
          ...updates,
          ...(options?.resetPage === false ? {} : { page: 1 }),
        }),
      })
    },
    updatePage(page: number) {
      void typedNavigate({
        search: (prev) => ({
          ...prev,
          page,
        }),
      })
    },
    updatePageSize(pageSize: number) {
      void typedNavigate({
        search: (prev) => ({
          ...prev,
          pageSize,
          page: 1,
        }),
      })
    },
  }
}
