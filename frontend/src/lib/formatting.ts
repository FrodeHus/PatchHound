export function startCase(value: string) {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\b\w/g, (char) => char.toUpperCase())
}

export function formatDate(value: string) {
  return new Date(value).toLocaleDateString()
}

export function formatDateTime(value: string) {
  return new Date(value).toLocaleString()
}

export function formatNullableDateTime(value: string | null | undefined, fallback = '-') {
  return value ? formatDateTime(value) : fallback
}

export function formatUnknownValue(value: unknown): string {
  if (typeof value === 'string') {
    return value
  }

  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }

  if (value === null || value === undefined) {
    return '-'
  }

  return JSON.stringify(value)
}

export function looksLikeOpaqueId(value: string) {
  return value.length > 24 || value.includes('-')
}
