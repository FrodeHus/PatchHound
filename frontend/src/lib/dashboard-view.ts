export const dashboardViewPreferenceKey = 'patchhound:dashboard-view'

export type DashboardViewMode = 'executive' | 'operations' | 'owner' | 'security-manager' | 'technical-manager'
export type DashboardRole =
  | 'GlobalAdmin'
  | 'SecurityManager'
  | 'SecurityAnalyst'
  | 'AssetOwner'
  | 'Stakeholder'
  | 'Auditor'
  | 'TechnicalManager'
  | string

export function readDashboardViewPreference(): DashboardViewMode | null {
  if (typeof window === 'undefined') {
    return null
  }

  const value = window.localStorage.getItem(dashboardViewPreferenceKey)
  return value === 'executive'
    || value === 'operations'
    || value === 'owner'
    || value === 'security-manager'
    || value === 'technical-manager'
    ? value
    : null
}

export function writeDashboardViewPreference(mode: DashboardViewMode) {
  if (typeof window === 'undefined') {
    return
  }

  window.localStorage.setItem(dashboardViewPreferenceKey, mode)
}

export function resolveDefaultDashboardView(roles: DashboardRole[]): DashboardViewMode {
  if (roles.includes('Stakeholder')) {
    return 'executive'
  }
  if (roles.includes('SecurityManager')) {
    return 'security-manager'
  }
  if (roles.includes('TechnicalManager')) {
    return 'technical-manager'
  }
  if (roles.includes('SecurityAnalyst')) {
    return 'operations'
  }
  if (roles.includes('AssetOwner')) {
    return 'owner'
  }
  return 'operations'
}

export function resolveDashboardViewMode({
  roles,
  requestedMode,
  preferredMode,
}: {
  roles: DashboardRole[]
  requestedMode?: DashboardViewMode | undefined
  preferredMode?: DashboardViewMode | null | undefined
}) {
  const defaultMode = resolveDefaultDashboardView(roles)
  const canSwitchModes = roles.includes('GlobalAdmin')
  return canSwitchModes ? requestedMode ?? preferredMode ?? defaultMode : defaultMode
}
