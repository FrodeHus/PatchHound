import type { DecisionSla } from '@/api/remediation.schemas'
import { cn } from '@/lib/utils'
import { formatDateTime } from '@/lib/formatting'

type SlaIndicatorProps = {
  sla: DecisionSla
}

export function SlaIndicator({ sla }: SlaIndicatorProps) {
  const statusConfig = {
    OnTrack: {
      label: 'On Track',
      bg: 'bg-emerald-500/12',
      bar: 'bg-emerald-500',
      text: 'text-emerald-700 dark:text-emerald-300',
    },
    NearDue: {
      label: 'Near Due',
      bg: 'bg-amber-500/12',
      bar: 'bg-amber-500',
      text: 'text-amber-700 dark:text-amber-300',
    },
    Overdue: {
      label: 'Overdue',
      bg: 'bg-red-500/12',
      bar: 'bg-red-500',
      text: 'text-red-700 dark:text-red-300',
    },
  } as const

  const config = statusConfig[sla.slaStatus as keyof typeof statusConfig] ?? statusConfig.OnTrack

  return (
    <div
      className={cn(
        'flex flex-wrap items-center justify-end gap-x-4 gap-y-2 rounded-full border border-border/70 px-3.5 py-2',
        config.bg
      )}
    >
      <div className="flex items-center gap-2">
        <div className={cn('h-2 w-2 rounded-full', config.bar)} />
        <span className={cn('text-sm font-medium', config.text)}>SLA {config.label}</span>
      </div>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
        {sla.dueDate ? (
          <span>
            <span className="font-medium text-foreground/80">Due</span> {formatDateTime(sla.dueDate)}
          </span>
        ) : null}
        <span className="hidden md:inline">
          <span className="font-medium text-foreground/80">Targets</span> {sla.criticalDays}d / {sla.highDays}d / {sla.mediumDays}d / {sla.lowDays}d
        </span>
      </div>
    </div>
  )
}
