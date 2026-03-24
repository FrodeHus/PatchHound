import { ApiRequestError } from '@/server/api'

export function getApiErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiRequestError) {
    return normalizeWhitespace(error.message, fallback)
  }

  if (error instanceof Error && error.message.trim()) {
    return normalizeWhitespace(error.message, fallback)
  }

  return fallback
}

function normalizeWhitespace(message: string, fallback: string) {
  const normalized = message
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)
    .join('\n')

  return normalized || fallback
}
