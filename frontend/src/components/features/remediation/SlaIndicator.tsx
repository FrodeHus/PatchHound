import type { DecisionSla } from '@/api/remediation.schemas'
import { cn } from '@/lib/utils'
import { formatDateTime } from '@/lib/formatting'

type SlaIndicatorProps = {
  sla: DecisionSla
}

export function SlaIndicator({ sla }: SlaIndicatorProps) {
  const statusConfig = {
    OnTrack: { label: 'On Track', bg: 'bg-emerald-500/20', bar: 'bg-emerald-500', text: 'text-emerald-600 dark:text-emerald-400' },
    NearDue: { label: 'Near Due', bg: 'bg-amber-500/20', bar: 'bg-amber-500', text: 'text-amber-600 dark:text-amber-400' },
    Overdue: { label: 'Overdue', bg: 'bg-red-500/20', bar: 'bg-red-500', text: 'text-red-600 dark:text-red-400' },
  } as const

  const config = statusConfig[sla.slaStatus as keyof typeof statusConfig] ?? statusConfig.OnTrack

  return (
    <div className={cn('flex items-center justify-between gap-4 rounded-lg border border-border/70 px-4 py-2.5', config.bg)}>
      <div className="flex items-center gap-3">
        <div className={cn('h-2.5 w-2.5 rounded-full', config.bar)} />
        <span className={cn('text-sm font-medium', config.text)}>
          SLA: {config.label}
        </span>
      </div>
      <div className="flex items-center gap-4 text-xs text-muted-foreground">
        {sla.dueDate ? (
          <span>Due: {formatDateTime(sla.dueDate)}</span>
        ) : null}
        <span className="hidden sm:inline">
          Targets: {sla.criticalDays}d / {sla.highDays}d / {sla.mediumDays}d / {sla.lowDays}d
        </span>
      </div>
    </div>
  )
}
