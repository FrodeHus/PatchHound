import { ConfidentialClientApplication } from '@azure/msal-node'

const ENTRA_CLIENT_ID = process.env.ENTRA_CLIENT_ID!
const ENTRA_CLIENT_SECRET = process.env.ENTRA_CLIENT_SECRET!
const ENTRA_TENANT_ID = process.env.ENTRA_TENANT_ID!
const ENTRA_REDIRECT_URI = process.env.ENTRA_REDIRECT_URI!
const ENTRA_SCOPES = process.env.ENTRA_SCOPES ?? 'openid profile email'

const AUTHORITY = `https://login.microsoftonline.com/${ENTRA_TENANT_ID}`
const SCOPES = ENTRA_SCOPES.split(/\s+/).filter(Boolean)

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

const GRAPH_SCOPE = 'https://graph.microsoft.com/.default'

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

export async function exchangeCodeForTokens(code: string): Promise<{
  access_token: string
  expires_in: number
  id_token?: string
  claims?: IdTokenClaims
  home_account_id?: string
}> {
  const result = await msalClient.acquireTokenByCode({
    code,
    scopes: SCOPES,
    redirectUri: ENTRA_REDIRECT_URI,
  })

  if (!result?.accessToken) {
    throw new Error('Token exchange failed: access token missing')
  }

  const expiresIn = result.expiresOn
    ? Math.max(60, Math.floor((result.expiresOn.getTime() - Date.now()) / 1000))
    : 3600

  return {
    access_token: result.accessToken,
    expires_in: expiresIn,
    id_token: result.idToken,
    claims: result.idTokenClaims as IdTokenClaims | undefined,
    home_account_id: result.account?.homeAccountId,
  }
}

export async function refreshAccessToken(homeAccountId: string): Promise<{
  access_token: string
  expires_in: number
}> {
  const account = await msalClient.getTokenCache().getAccountByHomeId(homeAccountId)
  if (!account) {
    throw new Error('Token refresh failed: account not found in cache')
  }

  const result = await msalClient.acquireTokenSilent({
    account,
    scopes: SCOPES,
    forceRefresh: true,
  })

  if (!result?.accessToken) {
    throw new Error('Token refresh failed: access token missing')
  }

  const expiresIn = result.expiresOn
    ? Math.max(60, Math.floor((result.expiresOn.getTime() - Date.now()) / 1000))
    : 3600

  return {
    access_token: result.accessToken,
    expires_in: expiresIn,
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
