export type ThemeOption = {
  id: string
  label: string
  mode: 'light' | 'dark'
}

export const themeOptions: ThemeOption[] = [
  { id: 'patchhound', label: 'PatchHound', mode: 'dark' },
  { id: 'light', label: 'Light', mode: 'light' },
  { id: 'solarized', label: 'Solarized Dark', mode: 'dark' },
  { id: 'cyberpunk', label: 'Cyberpunk', mode: 'dark' },
  { id: 'hackthebox', label: 'Hack The Box', mode: 'dark' },
  { id: 'catppuccin-latte', label: 'Catppuccin Latte', mode: 'light' },
  { id: 'catppuccin-frappe', label: 'Catppuccin Frappe', mode: 'dark' },
  { id: 'catppuccin-macchiato', label: 'Catppuccin Macchiato', mode: 'dark' },
  { id: 'catppuccin-mocha', label: 'Catppuccin Mocha', mode: 'dark' },
  { id: 'liquid-glass', label: 'Liquid Glass · Light', mode: 'light' },
  { id: 'liquid-glass-dark', label: 'Liquid Glass · Dark', mode: 'dark' },
]

export const defaultThemeId = 'patchhound'
export const themeStorageKey = 'patchhound:theme'

export function getTheme(themeId: string | null | undefined): ThemeOption {
  return themeOptions.find((theme) => theme.id === themeId) ?? themeOptions[0]
}

export function applyTheme(themeId: string | null | undefined) {
  if (typeof document === 'undefined') {
    return
  }

  const theme = getTheme(themeId)
  document.documentElement.dataset.theme = theme.id
  document.documentElement.classList.toggle('dark', theme.mode === 'dark')
  document.documentElement.style.colorScheme = theme.mode
}
