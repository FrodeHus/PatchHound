import { useState } from 'react'
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

function getInitialThemeId() {
  if (typeof window === 'undefined') {
    return defaultThemeId
  }

  return getTheme(window.localStorage.getItem(themeStorageKey)).id
}

export function ThemeSelector() {
  const [themeId, setThemeId] = useState(getInitialThemeId)

  return (
    <Select
      value={themeId}
      onValueChange={(nextThemeId) => {
        if (!nextThemeId) {
          return
        }

        setThemeId(nextThemeId)
        window.localStorage.setItem(themeStorageKey, nextThemeId)
        applyTheme(nextThemeId)
      }}
    >
      <SelectTrigger className="h-10 w-full justify-between rounded-xl border-border/70 bg-background/55 px-3" aria-label="Select theme">
        <div className="flex items-center gap-2">
          <Palette className="size-4 text-muted-foreground" />
          <span className="text-sm text-muted-foreground">Theme</span>
        </div>
        <SelectValue placeholder="Theme" />
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
