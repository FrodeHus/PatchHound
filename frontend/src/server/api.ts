const API_BASE = process.env.API_BASE_URL ?? 'http://localhost:8080/api'

export type ApiRequestContext = {
  token: string
  tenantId?: string
}

function buildHeaders(context: ApiRequestContext, includeJsonContentType = false) {
  const headers: Record<string, string> = {}

  if (context.token) {
    headers.Authorization = `Bearer ${context.token}`
  }

  if (context.tenantId) {
    headers['X-Tenant-Id'] = context.tenantId
  }

  if (includeJsonContentType) {
    headers['Content-Type'] = 'application/json'
  }

  return headers
}

async function parseJsonResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) {
    return null as T
  }

  const contentLength = response.headers.get('content-length')
  if (contentLength === '0') {
    return null as T
  }

  const text = await response.text()
  if (!text.trim()) {
    return null as T
  }

  return JSON.parse(text) as T
}

export async function apiGet<T>(path: string, context: ApiRequestContext): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: buildHeaders(context),
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  return parseJsonResponse<T>(response)
}

export async function apiPost<T>(path: string, context: ApiRequestContext, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: buildHeaders(context, true),
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  return parseJsonResponse<T>(response)
}

export async function apiPut<T>(path: string, context: ApiRequestContext, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'PUT',
    headers: buildHeaders(context, true),
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  return parseJsonResponse<T>(response)
}

export async function apiDelete<T>(path: string, context: ApiRequestContext): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'DELETE',
    headers: buildHeaders(context),
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  return parseJsonResponse<T>(response)
}
