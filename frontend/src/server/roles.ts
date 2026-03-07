const roleMap: Record<string, string> = {
  'Tenant.Admin': 'GlobalAdmin',
  'Tenant.SecurityManager': 'SecurityManager',
  'Tenant.SecurityAnalyst': 'SecurityAnalyst',
  'Tenant.AssetOwner': 'AssetOwner',
  'Tenant.Stakeholder': 'Stakeholder',
  'Tenant.Auditor': 'Auditor',
}

export function normalizeRoles(roles: string[] | undefined): string[] {
  const mapped = (roles ?? []).map((role) => roleMap[role] ?? role)
  return [...new Set(mapped)]
}
