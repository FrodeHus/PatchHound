import { getAccessToken } from '@/lib/auth'
import type { ProblemDetails } from '@/types/api'

const API_BASE = import.meta.env.VITE_API_URL ?? '/api'

export class ApiError extends Error {
  status: number
  problem: ProblemDetails | string | null

  constructor(status: number, problem: ProblemDetails | string | null) {
    const message = typeof problem === 'string' ? problem : problem?.title ?? 'Request failed'
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

function resolveHeaders(headers?: HeadersInit): Headers {
  return new Headers(headers)
}

function isJsonResponse(contentType: string | null): boolean {
  return contentType?.includes('application/json') ?? false
}

async function parseResponse(response: Response): Promise<unknown> {
  if (response.status === 204) {
    return null
  }

  if (isJsonResponse(response.headers.get('content-type'))) {
    return response.json()
  }

  return response.text()
}

export async function fetchWithAuth<T>(path: string, options?: RequestInit): Promise<T> {
  const token = await getAccessToken()
  const headers = resolveHeaders(options?.headers)

  if (!headers.has('Content-Type') && options?.body) {
    headers.set('Content-Type', 'application/json')
  }

  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers,
  })

  const body = await parseResponse(response)
  if (!response.ok) {
    throw new ApiError(response.status, body as ProblemDetails | string | null)
  }

  return body as T
}

export const apiClient = {
  get: <T>(path: string) => fetchWithAuth<T>(path),
  post: <T>(path: string, body?: unknown) =>
    fetchWithAuth<T>(path, {
      method: 'POST',
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  put: <T>(path: string, body?: unknown) =>
    fetchWithAuth<T>(path, {
      method: 'PUT',
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  patch: <T>(path: string, body?: unknown) =>
    fetchWithAuth<T>(path, {
      method: 'PATCH',
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  delete: <T>(path: string) =>
    fetchWithAuth<T>(path, {
      method: 'DELETE',
    }),
}
