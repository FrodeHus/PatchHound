import crypto from 'node:crypto'
import { Pool } from 'pg'
import { getCookie, setCookie } from '@tanstack/react-start/server'

const ENCRYPTION_ALGORITHM = 'aes-256-gcm'
const IV_LENGTH = 12
const AUTH_TAG_LENGTH = 16

function getEncryptionKey(): Buffer | null {
  const hex = process.env.SESSION_ENCRYPTION_KEY
  if (!hex) return null
  const buf = Buffer.from(hex, 'hex')
  if (buf.length !== 32) {
    throw new Error('SESSION_ENCRYPTION_KEY must be exactly 32 bytes (64 hex characters)')
  }
  return buf
}

function encryptPayload(plaintext: string): string {
  const key = getEncryptionKey()
  if (!key) return plaintext

  const iv = crypto.randomBytes(IV_LENGTH)
  const cipher = crypto.createCipheriv(ENCRYPTION_ALGORITHM, key, iv, { authTagLength: AUTH_TAG_LENGTH })
  const encrypted = Buffer.concat([cipher.update(plaintext, 'utf8'), cipher.final()])
  const authTag = cipher.getAuthTag()

  // Format: base64(iv + authTag + ciphertext) prefixed with "enc:" marker
  return 'enc:' + Buffer.concat([iv, authTag, encrypted]).toString('base64')
}

function decryptPayload(stored: string): string {
  if (!stored.startsWith('enc:')) return stored

  const key = getEncryptionKey()
  if (!key) {
    throw new Error('SESSION_ENCRYPTION_KEY is required to decrypt session data')
  }

  const raw = Buffer.from(stored.slice(4), 'base64')
  const iv = raw.subarray(0, IV_LENGTH)
  const authTag = raw.subarray(IV_LENGTH, IV_LENGTH + AUTH_TAG_LENGTH)
  const ciphertext = raw.subarray(IV_LENGTH + AUTH_TAG_LENGTH)

  const decipher = crypto.createDecipheriv(ENCRYPTION_ALGORITHM, key, iv, { authTagLength: AUTH_TAG_LENGTH })
  decipher.setAuthTag(authTag)
  const decrypted = Buffer.concat([decipher.update(ciphertext), decipher.final()])
  return decrypted.toString('utf8')
}

export interface SessionData {
  accessToken?: string
  tokenExpiry?: number
  refreshToken?: string
  userId?: string
  email?: string
  displayName?: string
  tenantId?: string
  tenantName?: string
  entraRoles?: string[]
  roles?: string[]
  tenantIds?: string[]
  oauthState?: string
  activeRoles?: string[]
}

const COOKIE_NAME = 'patchhound-session'
const SESSION_TTL_SECONDS = 7 * 24 * 60 * 60
const cookieOptions = {
  secure: process.env.COOKIE_SECURE === 'true' || process.env.NODE_ENV === 'production',
  httpOnly: true,
  sameSite: 'lax' as const,
  maxAge: SESSION_TTL_SECONDS,
  path: '/',
}

declare global {
  var __patchhoundFrontendSessionPool: Pool | undefined
  var __patchhoundFrontendSessionTableReady: Promise<void> | undefined
}

function resolveSessionDatabaseUrl() {
  if (process.env.SESSION_DATABASE_URL) {
    return process.env.SESSION_DATABASE_URL
  }

  if (process.env.DATABASE_URL) {
    return process.env.DATABASE_URL
  }

  const db = process.env.POSTGRES_DB
  const user = process.env.POSTGRES_USER
  const password = process.env.POSTGRES_PASSWORD
  const host = process.env.POSTGRES_HOST ?? 'localhost'
  const port = process.env.POSTGRES_PORT ?? '5432'

  if (db && user && password) {
    return `postgresql://${encodeURIComponent(user)}:${encodeURIComponent(password)}@${host}:${port}/${db}`
  }

  throw new Error('SESSION_DATABASE_URL is not configured')
}

function getPool() {
  globalThis.__patchhoundFrontendSessionPool ??= new Pool({
    connectionString: resolveSessionDatabaseUrl(),
    max: 5,
  })

  return globalThis.__patchhoundFrontendSessionPool
}

async function ensureSessionTable() {
  globalThis.__patchhoundFrontendSessionTableReady ??= (async () => {
    const pool = getPool()
    await pool.query(`
      CREATE TABLE IF NOT EXISTS frontend_sessions (
        id character varying(128) PRIMARY KEY,
        data jsonb NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL
      );
    `)
    await pool.query(`
      CREATE INDEX IF NOT EXISTS ix_frontend_sessions_expires_at
      ON frontend_sessions (expires_at);
    `)
  })()

  await globalThis.__patchhoundFrontendSessionTableReady
}

function generateSessionId() {
  return crypto.randomBytes(32).toString('hex')
}

function clearSessionCookie() {
  setCookie(COOKIE_NAME, '', {
    ...cookieOptions,
    maxAge: 0,
  })
}

class AppSession implements SessionData {
  accessToken?: string
  tokenExpiry?: number
  refreshToken?: string
  userId?: string
  email?: string
  displayName?: string
  tenantId?: string
  tenantName?: string
  entraRoles?: string[]
  roles?: string[]
  tenantIds?: string[]
  oauthState?: string
  activeRoles?: string[]

  private sid?: string

  constructor(data?: SessionData, sid?: string) {
    Object.assign(this, data)
    this.sid = sid
  }

  async save() {
    await ensureSessionTable()

    this.sid ??= generateSessionId()

    const payload: SessionData = {
      accessToken: this.accessToken,
      tokenExpiry: this.tokenExpiry,
      refreshToken: this.refreshToken,
      userId: this.userId,
      email: this.email,
      displayName: this.displayName,
      tenantId: this.tenantId,
      tenantName: this.tenantName,
      entraRoles: this.entraRoles,
      roles: this.roles,
      tenantIds: this.tenantIds,
      oauthState: this.oauthState,
      activeRoles: this.activeRoles,
    }

    const expiresAt = new Date(Date.now() + SESSION_TTL_SECONDS * 1000)
    const pool = getPool()
    const serialized = encryptPayload(JSON.stringify(payload))
    await pool.query(
      `
      INSERT INTO frontend_sessions (id, data, expires_at, updated_at)
      VALUES ($1, $2::jsonb, $3, NOW())
      ON CONFLICT (id)
      DO UPDATE SET data = EXCLUDED.data, expires_at = EXCLUDED.expires_at, updated_at = NOW()
      `,
      [this.sid, JSON.stringify(serialized), expiresAt],
    )

    setCookie(COOKIE_NAME, this.sid, cookieOptions)
  }

  async destroy() {
    await ensureSessionTable()

    if (this.sid) {
      await getPool().query('DELETE FROM frontend_sessions WHERE id = $1', [this.sid])
    }

    this.sid = undefined
    clearSessionCookie()
  }

  async regenerate() {
    await ensureSessionTable()

    const previousSid = this.sid
    this.sid = generateSessionId()

    if (previousSid) {
      await getPool().query('DELETE FROM frontend_sessions WHERE id = $1', [previousSid])
    }

    // Cookie is written by the subsequent save() call, not here.
    // Setting it before the row exists creates a dangling cookie if save() throws.
  }
}

export async function getSession() {
  await ensureSessionTable()

  const sid = getCookie(COOKIE_NAME)
  if (!sid) {
    return new AppSession()
  }

  const result = await getPool().query<{
    data: string | SessionData
    expires_at: Date
  }>(
    `
    SELECT data, expires_at
    FROM frontend_sessions
    WHERE id = $1
    LIMIT 1
    `,
    [sid],
  )

  const record = result.rows[0]
  if (!record) {
    clearSessionCookie()
    return new AppSession()
  }

  if (record.expires_at.getTime() <= Date.now()) {
    await getPool().query('DELETE FROM frontend_sessions WHERE id = $1', [sid])
    clearSessionCookie()
    return new AppSession()
  }

  let sessionData: SessionData
  if (typeof record.data === 'string') {
    // Encrypted or legacy string payload
    sessionData = JSON.parse(decryptPayload(record.data)) as SessionData
  } else {
    // Legacy unencrypted JSONB payload (before encryption was enabled)
    sessionData = record.data
  }

  return new AppSession(sessionData, sid)
}

export function isTokenExpired(session: SessionData): boolean {
  if (!session.tokenExpiry) return true
  return Date.now() > session.tokenExpiry - 5 * 60 * 1000
}
