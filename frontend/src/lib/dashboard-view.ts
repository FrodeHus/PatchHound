export const dashboardViewPreferenceKey = 'patchhound:dashboard-view'

export type DashboardViewMode = 'executive' | 'operations'

export function readDashboardViewPreference(): DashboardViewMode | null {
  if (typeof window === 'undefined') {
    return null
  }

  const value = window.localStorage.getItem(dashboardViewPreferenceKey)
  return value === 'executive' || value === 'operations' ? value : null
}

export function writeDashboardViewPreference(mode: DashboardViewMode) {
  if (typeof window === 'undefined') {
    return
  }

  window.localStorage.setItem(dashboardViewPreferenceKey, mode)
}
