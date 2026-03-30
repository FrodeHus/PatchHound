const roleMap: Record<string, string> = {
  'Tenant.Admin': 'GlobalAdmin',
  'Tenant.SecurityManager': 'SecurityManager',
  'Tenant.SecurityAnalyst': 'SecurityAnalyst',
  'Tenant.AssetOwner': 'AssetOwner',
  'Tenant.Stakeholder': 'Stakeholder',
  'Tenant.Auditor': 'Auditor',
  GlobalAdmin: 'GlobalAdmin',
  SecurityManager: 'SecurityManager',
  SecurityAnalyst: 'SecurityAnalyst',
  AssetOwner: 'AssetOwner',
  Stakeholder: 'Stakeholder',
  Auditor: 'Auditor',
}

export function normalizeRoles(roles: string[] | undefined): string[] {
  const mapped = (roles ?? [])
    .map((role) => roleMap[role])
    .filter((role): role is string => Boolean(role))
  return [...new Set(mapped)]
}
