// frontend-start/src/server/session.ts
import { getIronSession } from 'iron-session'
import { getCookie, setCookie } from '@tanstack/react-start/server'

export interface SessionData {
  accessToken?: string
  tokenExpiry?: number
  userId?: string
  email?: string
  displayName?: string
  tenantId?: string
  roles?: string[]
  tenantIds?: string[]
  homeAccountId?: string
  oauthState?: string
}

type CookieOptions = Parameters<typeof setCookie>[2]
type CookieDescriptor = {
  name: string
  value: string
} & Partial<NonNullable<CookieOptions>>

const sessionOptions = {
  password: process.env.SESSION_SECRET!,
  cookieName: 'patchhound-session',
  cookieOptions: {
    secure: process.env.COOKIE_SECURE === 'true' || process.env.NODE_ENV === 'production',
    httpOnly: true,
    sameSite: 'lax' as const,
    maxAge: 7 * 24 * 60 * 60, // 7 days
  },
}

export async function getSession() {
  const cookies = {
    get(name: string) {
      const value = getCookie(name)
      return value ? { name, value } : undefined
    },
    set(
      nameOrOptions: string | CookieDescriptor,
      value?: string,
      cookie?: CookieOptions,
    ) {
      if (typeof nameOrOptions === 'string') {
        if (value === undefined) {
          throw new Error(`Missing cookie value for ${nameOrOptions}`)
        }

        setCookie(nameOrOptions, value, cookie)
        return
      }

      const { name, value: optionValue, ...options } = nameOrOptions
      setCookie(name, optionValue, options)
    },
  }

  return getIronSession<SessionData>(cookies, sessionOptions)
}

export function isTokenExpired(session: SessionData): boolean {
  if (!session.tokenExpiry) return true
  // Refresh 5 minutes before expiry
  return Date.now() > (session.tokenExpiry - 5 * 60 * 1000)
}
