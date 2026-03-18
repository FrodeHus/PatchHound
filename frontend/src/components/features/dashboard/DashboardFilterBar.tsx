import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { DashboardFilterOptions } from '@/api/dashboard.schemas'

type DashboardFilterBarProps = {
  minAgeDays: string
  platform: string
  deviceGroup: string
  filterOptions: DashboardFilterOptions | undefined
  onMinAgeDaysChange: (value: string) => void
  onPlatformChange: (value: string) => void
  onDeviceGroupChange: (value: string) => void
}

const ageOptions = [
  { label: 'All ages', value: '' },
  { label: '\u2265 30 days', value: '30' },
  { label: '\u2265 90 days', value: '90' },
  { label: '\u2265 180 days', value: '180' },
]

export function DashboardFilterBar({
  minAgeDays,
  platform,
  deviceGroup,
  filterOptions,
  onMinAgeDaysChange,
  onPlatformChange,
  onDeviceGroupChange,
}: DashboardFilterBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">Filters</p>

      <Select value={minAgeDays || 'all'} onValueChange={(v) => onMinAgeDaysChange(v === 'all' || v === null ? '' : v)}>
        <SelectTrigger className="h-9 min-w-[140px] rounded-xl border-border/70 bg-background/80 px-3">
          <SelectValue placeholder="All ages" />
        </SelectTrigger>
        <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
          {ageOptions.map((opt) => (
            <SelectItem key={opt.value || 'all'} value={opt.value || 'all'}>{opt.label}</SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select value={platform || 'all'} onValueChange={(v) => onPlatformChange(v === 'all' || v === null ? '' : v)}>
        <SelectTrigger className="h-9 min-w-[150px] rounded-xl border-border/70 bg-background/80 px-3">
          <SelectValue placeholder="All platforms" />
        </SelectTrigger>
        <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
          <SelectItem value="all">All platforms</SelectItem>
          {filterOptions?.platforms.map((p) => (
            <SelectItem key={p} value={p}>{p}</SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select value={deviceGroup || 'all'} onValueChange={(v) => onDeviceGroupChange(v === 'all' || v === null ? '' : v)}>
        <SelectTrigger className="h-9 min-w-[160px] rounded-xl border-border/70 bg-background/80 px-3">
          <SelectValue placeholder="All groups" />
        </SelectTrigger>
        <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
          <SelectItem value="all">All groups</SelectItem>
          {filterOptions?.deviceGroups.map((g) => (
            <SelectItem key={g} value={g}>{g}</SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}
