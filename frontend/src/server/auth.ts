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
  roles?: string[]
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
