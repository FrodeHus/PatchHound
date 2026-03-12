import { ConfidentialClientApplication } from '@azure/msal-node'

const ENTRA_CLIENT_ID = process.env.ENTRA_CLIENT_ID!
const ENTRA_CLIENT_SECRET = process.env.ENTRA_CLIENT_SECRET!
const ENTRA_TENANT_ID = process.env.ENTRA_TENANT_ID!
const ENTRA_REDIRECT_URI = process.env.ENTRA_REDIRECT_URI!
const ENTRA_SCOPES = process.env.ENTRA_SCOPES ?? 'openid profile email'

const AUTHORITY = `https://login.microsoftonline.com/${ENTRA_TENANT_ID}`
const SCOPES = Array.from(
  new Set([...ENTRA_SCOPES.split(/\s+/).filter(Boolean), 'openid', 'profile', 'email', 'offline_access']),
)

const msalClient = new ConfidentialClientApplication({
  auth: {
    clientId: ENTRA_CLIENT_ID,
    clientSecret: ENTRA_CLIENT_SECRET,
    authority: AUTHORITY,
  },
})

export type IdTokenClaims = {
  oid?: string
  preferred_username?: string
  name?: string
  tid?: string
  tenant_display_name?: string
  tenant_name?: string
  roles?: string[]
  [key: string]: unknown
}

type TokenExchangeResult = {
  access_token: string
  expires_in: number
  id_token?: string
  claims?: IdTokenClaims
  refresh_token?: string
}

type TokenRefreshResult = {
  access_token: string
  expires_in: number
  refresh_token?: string
}

const GRAPH_SCOPE = 'https://graph.microsoft.com/.default'
const TOKEN_ENDPOINT = `https://login.microsoftonline.com/${ENTRA_TENANT_ID}/oauth2/v2.0/token`

export function getClaimString(
  claims: IdTokenClaims | undefined,
  claimNames: string[],
): string | undefined {
  if (!claims) {
    return undefined
  }

  for (const claimName of claimNames) {
    const value = claims[claimName]
    if (typeof value === 'string' && value.trim().length > 0) {
      return value.trim()
    }
  }

  return undefined
}

export async function getAuthorizationUrl(state: string): Promise<string> {
  return msalClient.getAuthCodeUrl({
    scopes: SCOPES,
    redirectUri: ENTRA_REDIRECT_URI,
    state,
  })
}

export async function exchangeCodeForTokens(code: string): Promise<TokenExchangeResult> {
  const body = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    client_secret: ENTRA_CLIENT_SECRET,
    code,
    grant_type: 'authorization_code',
    redirect_uri: ENTRA_REDIRECT_URI,
    scope: SCOPES.join(' '),
  })

  const response = await fetch(TOKEN_ENDPOINT, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body,
  })

  if (!response.ok) {
    throw new Error(`Token exchange failed: ${response.status} ${response.statusText}`)
  }

  const result = await response.json() as {
    access_token?: string
    expires_in?: number
    id_token?: string
    refresh_token?: string
  }

  if (!result.access_token) {
    throw new Error('Token exchange failed: access token missing')
  }

  const expiresIn = typeof result.expires_in === 'number' && result.expires_in > 0
    ? Math.max(60, result.expires_in)
    : 3600
  const claims = decodeJwtClaims(result.id_token)

  return {
    access_token: result.access_token,
    expires_in: expiresIn,
    id_token: result.id_token,
    claims,
    refresh_token: result.refresh_token,
  }
}

export async function refreshAccessTokenByRefreshToken(refreshToken: string): Promise<TokenRefreshResult> {
  const body = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    client_secret: ENTRA_CLIENT_SECRET,
    grant_type: 'refresh_token',
    refresh_token: refreshToken,
    scope: SCOPES.join(' '),
  })

  const response = await fetch(TOKEN_ENDPOINT, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body,
  })

  if (!response.ok) {
    throw new Error(`Token refresh failed: ${response.status} ${response.statusText}`)
  }

  const result = await response.json() as {
    access_token?: string
    expires_in?: number
    refresh_token?: string
  }

  if (!result.access_token) {
    throw new Error('Token refresh failed: access token missing')
  }

  const expiresIn = typeof result.expires_in === 'number' && result.expires_in > 0
    ? Math.max(60, result.expires_in)
    : 3600

  return {
    access_token: result.access_token,
    expires_in: expiresIn,
    refresh_token: result.refresh_token,
  }
}

function decodeJwtClaims(idToken?: string): IdTokenClaims | undefined {
  if (!idToken) {
    return undefined
  }

  const [, payload] = idToken.split('.')
  if (!payload) {
    return undefined
  }

  const normalized = payload.replace(/-/g, '+').replace(/_/g, '/')
  const padded = normalized.padEnd(normalized.length + ((4 - normalized.length % 4) % 4), '=')

  try {
    const decoded = Buffer.from(padded, 'base64').toString('utf8')
    return JSON.parse(decoded) as IdTokenClaims
  } catch {
    return undefined
  }
}

export async function resolveTenantDisplayName(
  tenantId: string,
  claims?: IdTokenClaims,
): Promise<string> {
  const claimValue = getClaimString(claims, ['tenant_display_name', 'tenant_name'])
  if (claimValue) {
    return claimValue
  }

  const graphClient = new ConfidentialClientApplication({
    auth: {
      clientId: ENTRA_CLIENT_ID,
      clientSecret: ENTRA_CLIENT_SECRET,
      authority: `https://login.microsoftonline.com/${tenantId}`,
    },
  })

  const tokenResult = await graphClient.acquireTokenByClientCredential({
    scopes: [GRAPH_SCOPE],
  })

  if (!tokenResult?.accessToken) {
    throw new Error('Tenant directory lookup failed: Graph access token missing')
  }

  const response = await fetch(
    'https://graph.microsoft.com/v1.0/organization?$select=id,displayName',
    {
      headers: {
        Authorization: `Bearer ${tokenResult.accessToken}`,
      },
    },
  )

  if (!response.ok) {
    throw new Error(`Tenant directory lookup failed: ${response.status} ${response.statusText}`)
  }

  const data = await response.json() as {
    value?: Array<{ id?: string; displayName?: string }>
  }

  const organization = data.value?.find((entry) => entry.id === tenantId) ?? data.value?.[0]
  if (!organization?.displayName?.trim()) {
    throw new Error('Tenant directory lookup failed: display name missing')
  }

  return organization.displayName.trim()
}
