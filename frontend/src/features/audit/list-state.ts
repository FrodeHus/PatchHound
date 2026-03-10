type AuditLogListSearch = {
  action: string
  entityType: string
  page: number
  pageSize: number
}

export function buildAuditLogListRequest(search: AuditLogListSearch) {
  return {
    ...(search.action ? { action: search.action } : {}),
    ...(search.entityType ? { entityType: search.entityType } : {}),
    page: search.page,
    pageSize: search.pageSize,
  }
}

export const auditQueryKeys = {
  all: ['audit-log'] as const,
  list: (search: AuditLogListSearch) => [
    ...auditQueryKeys.all,
    'list',
    search.action,
    search.entityType,
    search.page,
    search.pageSize,
  ] as const,
}
