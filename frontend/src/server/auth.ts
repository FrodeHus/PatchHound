const ENTRA_CLIENT_ID = process.env.ENTRA_CLIENT_ID!
const ENTRA_CLIENT_SECRET = process.env.ENTRA_CLIENT_SECRET!
const ENTRA_TENANT_ID = process.env.ENTRA_TENANT_ID!
const ENTRA_REDIRECT_URI = process.env.ENTRA_REDIRECT_URI!
const ENTRA_SCOPES = process.env.ENTRA_SCOPES ?? 'openid profile email'

const AUTHORITY = `https://login.microsoftonline.com/${ENTRA_TENANT_ID}/oauth2/v2.0`

export function getAuthorizationUrl(state: string): string {
  const params = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    response_type: 'code',
    redirect_uri: ENTRA_REDIRECT_URI,
    scope: ENTRA_SCOPES,
    response_mode: 'query',
    state,
  })
  return `${AUTHORITY}/authorize?${params.toString()}`
}

export async function exchangeCodeForTokens(code: string) {
  const params = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    client_secret: ENTRA_CLIENT_SECRET,
    grant_type: 'authorization_code',
    code,
    redirect_uri: ENTRA_REDIRECT_URI,
    scope: ENTRA_SCOPES,
  })

  const response = await fetch(`${AUTHORITY}/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params.toString(),
  })

  if (!response.ok) {
    const error = await response.text()
    throw new Error(`Token exchange failed: ${error}`)
  }

  return response.json() as Promise<{
    access_token: string
    refresh_token?: string
    expires_in: number
    id_token: string
  }>
}

export async function refreshAccessToken(refreshToken: string) {
  const params = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    client_secret: ENTRA_CLIENT_SECRET,
    grant_type: 'refresh_token',
    refresh_token: refreshToken,
    scope: ENTRA_SCOPES,
  })

  const response = await fetch(`${AUTHORITY}/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params.toString(),
  })

  if (!response.ok) {
    throw new Error('Token refresh failed')
  }

  return response.json() as Promise<{
    access_token: string
    refresh_token?: string
    expires_in: number
  }>
}

export function parseIdToken(idToken: string): {
  oid?: string
  preferred_username?: string
  name?: string
  tid?: string
  roles?: string[]
} {
  const payload = idToken.split('.')[1]
  return JSON.parse(Buffer.from(payload, 'base64url').toString())
}
