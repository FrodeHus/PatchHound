export function parseAuditValues(raw: string | null) {
  if (!raw) {
    return {}
  }

  try {
    const parsed: unknown = JSON.parse(raw)
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

export function formatAuditEntityType(value: string) {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/Id\b/g, 'ID')
}

export function formatAuditKey(value: string) {
  return formatAuditEntityType(value).toLowerCase()
}
