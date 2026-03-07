import { useEffect, useState } from 'react'
import { Palette } from 'lucide-react'
import { applyTheme, defaultThemeId, getTheme, themeOptions, themeStorageKey } from '@/lib/themes'
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'

export function ThemeSelector() {
  const [themeId, setThemeId] = useState(defaultThemeId)

  useEffect(() => {
    const storedTheme = window.localStorage.getItem(themeStorageKey)
    const resolvedTheme = getTheme(storedTheme)
    setThemeId(resolvedTheme.id)
    applyTheme(resolvedTheme.id)
  }, [])

  return (
    <Select
      value={themeId}
      onValueChange={(nextThemeId) => {
        setThemeId(nextThemeId)
        window.localStorage.setItem(themeStorageKey, nextThemeId)
        applyTheme(nextThemeId)
      }}
    >
      <SelectTrigger
        className="h-11 min-w-44 rounded-full border-border/70 bg-background/55 px-3"
        aria-label="Select theme"
      >
        <div className="flex items-center gap-2">
          <Palette className="size-4 text-muted-foreground" />
          <SelectValue placeholder="Theme" />
        </div>
      </SelectTrigger>
      <SelectContent align="end" className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
        <SelectGroup>
          <SelectLabel>Theme</SelectLabel>
          {themeOptions.map((theme) => (
            <SelectItem key={theme.id} value={theme.id}>
              {theme.label}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  )
}
