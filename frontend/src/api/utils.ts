import type { ApiRequestContext } from '@/server/api'

export function buildFilterParams(
  filters: Record<string, string | number | boolean | undefined>,
  defaults: { page?: number; pageSize?: number } = {},
): URLSearchParams {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    if (key === 'page' || key === 'pageSize') continue
    if (value !== undefined && value !== '') {
      params.set(key, String(value))
    }
  }
  const page = (filters.page as number | undefined) ?? defaults.page ?? 1
  const pageSize = (filters.pageSize as number | undefined) ?? defaults.pageSize ?? 50
  params.set('page', String(page))
  params.set('pageSize', String(pageSize))
  return params
}

export function withTenantOverride(
  context: ApiRequestContext,
  tenantId?: string,
): ApiRequestContext {
  if (!tenantId) {
    return context
  }

  return {
    ...context,
    tenantId,
  }
}
