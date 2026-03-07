// frontend-start/src/server/session.ts
import { getIronSession } from 'iron-session'
import { getWebRequest } from '@tanstack/react-start/server'

export interface SessionData {
  accessToken?: string
  refreshToken?: string
  tokenExpiry?: number
  userId?: string
  email?: string
  displayName?: string
  tenantId?: string
  roles?: string[]
  tenantIds?: string[]
}

const sessionOptions = {
  password: process.env.SESSION_SECRET!,
  cookieName: 'vigil-session',
  cookieOptions: {
    secure: process.env.NODE_ENV === 'production',
    httpOnly: true,
    sameSite: 'lax' as const,
    maxAge: 7 * 24 * 60 * 60, // 7 days
  },
}

export async function getSession() {
  const request = getWebRequest()
  // iron-session works with a request/response pair
  // For reading, we parse the cookie from the request headers
  const cookieHeader = request.headers.get('cookie') ?? ''
  const mockReq = { headers: { cookie: cookieHeader } } as any
  const mockRes = { getHeader: () => '', setHeader: () => {} } as any
  return getIronSession<SessionData>(mockReq, mockRes, sessionOptions)
}

export function isTokenExpired(session: SessionData): boolean {
  if (!session.tokenExpiry) return true
  // Refresh 5 minutes before expiry
  return Date.now() > (session.tokenExpiry - 5 * 60 * 1000)
}
