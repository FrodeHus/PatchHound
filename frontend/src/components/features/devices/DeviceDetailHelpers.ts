export type MetadataRecord = Record<string, unknown>

export function parseMetadata(metadata: string | undefined): MetadataRecord {
  if (!metadata) {
    return {}
  }

  try {
    const parsed = JSON.parse(metadata) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as MetadataRecord)
      : {}
  } catch {
    return {}
  }
}

export function readString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null
}

export function readNumber(value: unknown): number | null {
  return typeof value === 'number' ? value : null
}

export function readBoolean(value: unknown): boolean {
  return value === true
}

export function getDefaultDescription(): string {
  return 'Endpoint or host device tracked through vulnerability and ownership workflows.'
}
