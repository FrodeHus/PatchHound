const API_BASE = process.env.API_BASE_URL ?? 'http://localhost:8080/api'

export type ApiRequestContext = {
  token: string
  tenantId?: string
  activeRoles?: string[]
}

export class ApiRequestError extends Error {
  status: number
  statusText: string
  bodyText: string | null

  constructor(message: string, status: number, statusText: string, bodyText: string | null) {
    super(message)
    this.name = 'ApiRequestError'
    this.status = status
    this.statusText = statusText
    this.bodyText = bodyText
  }
}

export class UnauthenticatedApiError extends ApiRequestError {
  constructor(statusText: string, bodyText: string | null) {
    super('Authentication required.', 401, statusText, bodyText)
    this.name = 'UnauthenticatedApiError'
  }
}

export class ForbiddenApiError extends ApiRequestError {
  constructor(statusText: string, bodyText: string | null) {
    super('You do not have access to perform this action.', 403, statusText, bodyText)
    this.name = 'ForbiddenApiError'
  }
}

export class TenantPendingDeletionError extends ApiRequestError {
  constructor(statusText: string, bodyText: string | null) {
    super('TENANT_PENDING_DELETION', 410, statusText, bodyText)
    this.name = 'TenantPendingDeletionError'
  }
}

export class ValidationApiError extends ApiRequestError {
  constructor(status: number, statusText: string, bodyText: string | null) {
    super(
      buildFriendlyErrorMessage(
        bodyText,
        'The request was rejected as invalid.',
      ),
      status,
      statusText,
      bodyText,
    )
    this.name = 'ValidationApiError'
  }
}

function buildHeaders(context: ApiRequestContext, includeJsonContentType = false) {
  const headers: Record<string, string> = {}

  if (context.token) {
    headers.Authorization = `Bearer ${context.token}`
  }

  if (context.tenantId) {
    headers['X-Tenant-Id'] = context.tenantId
  }

  if (context.activeRoles?.length) {
    headers['X-Active-Roles'] = context.activeRoles.join(',')
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

async function ensureOk(response: Response): Promise<void> {
  if (response.ok) {
    return
  }

  const bodyText = (await response.text()).trim() || null

  if (response.status === 410) {
    let errorCode: string | null = null
    try { errorCode = bodyText ? (JSON.parse(bodyText) as { errorCode?: string }).errorCode ?? null : null } catch { /* ignore */ }
    if (errorCode === 'tenant_pending_deletion') {
      throw new TenantPendingDeletionError(response.statusText, bodyText)
    }
  }

  if (response.status === 401) {
    throw new UnauthenticatedApiError(response.statusText, bodyText)
  }

  if (response.status === 403) {
    throw new ForbiddenApiError(response.statusText, bodyText)
  }

  if (response.status === 400 || response.status === 422) {
    throw new ValidationApiError(response.status, response.statusText, bodyText)
  }

  throw new ApiRequestError(
    buildFriendlyErrorMessage(
      bodyText,
      `API request failed with ${response.status} ${response.statusText}.`,
    ),
    response.status,
    response.statusText,
    bodyText,
  )
}

type ProblemDetailsShape = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

function buildFriendlyErrorMessage(bodyText: string | null, fallback: string) {
  if (!bodyText) {
    return fallback
  }

  const parsed = tryParseProblemDetails(bodyText)
  if (!parsed) {
    return bodyText || fallback
  }

  const parts: string[] = []
  if (parsed.title?.trim()) {
    parts.push(parsed.title.trim())
  }

  if (parsed.detail?.trim()) {
    parts.push(parsed.detail.trim())
  }

  const validationErrors = flattenValidationErrors(parsed.errors)
  if (validationErrors.length > 0) {
    parts.push(validationErrors.join(' '))
  }

  return parts.length > 0 ? parts.join(' ') : fallback
}

function tryParseProblemDetails(bodyText: string): ProblemDetailsShape | null {
  try {
    const parsed = JSON.parse(bodyText) as ProblemDetailsShape
    if (
      typeof parsed === 'object'
      && parsed !== null
      && ('title' in parsed || 'detail' in parsed || 'errors' in parsed)
    ) {
      return parsed
    }
  } catch {
    return null
  }

  return null
}

function flattenValidationErrors(errors?: Record<string, string[]>) {
  if (!errors) {
    return []
  }

  return Object.entries(errors)
    .flatMap(([field, messages]) =>
      messages.map((message) => (field ? `${field}: ${message}` : message)),
    )
}

export async function apiGet<T>(path: string, context: ApiRequestContext): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: buildHeaders(context),
  })

  await ensureOk(response)

  return parseJsonResponse<T>(response)
}

export async function apiPost<T>(path: string, context: ApiRequestContext, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: buildHeaders(context, true),
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  await ensureOk(response)

  return parseJsonResponse<T>(response)
}

export async function apiPut<T>(path: string, context: ApiRequestContext, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'PUT',
    headers: buildHeaders(context, true),
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  await ensureOk(response)

  return parseJsonResponse<T>(response)
}

export async function apiDelete<T>(path: string, context: ApiRequestContext): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'DELETE',
    headers: buildHeaders(context),
  })

  await ensureOk(response)

  return parseJsonResponse<T>(response)
}
