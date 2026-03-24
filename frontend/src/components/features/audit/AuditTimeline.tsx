import {
  CheckCircle2,
  Clock3,
  Info,
  PencilLine,
  Sparkles,
  ShieldAlert,
  Trash2,
  XCircle,
} from 'lucide-react'
import type { Tone } from '@/lib/tone-classes'
import { toneBadge } from '@/lib/tone-classes'
import { formatDateTime, startCase } from '@/lib/formatting'

export type AuditTimelineBadge = {
  label: string
  tone?: Tone
}

export type AuditTimelineEvent = {
  id: string
  action: string
  title: string
  description?: string | null
  timestamp: string
  badges?: AuditTimelineBadge[]
}

type AuditTimelineProps = {
  events: AuditTimelineEvent[]
  emptyMessage?: string
}

export function AuditTimeline({
  events,
  emptyMessage = 'No timeline events recorded yet.',
}: AuditTimelineProps) {
  if (events.length === 0) {
    return (
      <section className="rounded-2xl border border-border/70 bg-background/40 p-5 text-sm text-muted-foreground">
        {emptyMessage}
      </section>
    )
  }

  return (
    <ol className="space-y-4">
      {events.map((event, index) => {
        const accent = eventTone(event.action)
        const Icon = eventIcon(event.action)

        return (
          <li key={event.id} className="relative pl-14">
            {index < events.length - 1 ? (
              <span
                aria-hidden="true"
                className="absolute left-[1.1rem] top-11 bottom-[-1.5rem] w-px bg-border/70"
              />
            ) : null}
            <span
              className={`absolute left-0 top-1 inline-flex size-9 items-center justify-center rounded-full border shadow-sm ${toneBadge(accent)}`}
            >
              <Icon className="size-4" />
            </span>
            <div className="rounded-[1.4rem] border border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--background)_88%,transparent),var(--color-card))] px-5 py-4 shadow-[0_12px_30px_-28px_rgba(0,0,0,0.45)]">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="space-y-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <span
                      className={`inline-flex rounded-full border px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-[0.14em] ${toneBadge(accent)}`}
                    >
                      {startCase(event.action)}
                    </span>
                    {event.badges?.map((badge) => (
                      <span
                        key={`${event.id}-${badge.label}`}
                        className={`inline-flex rounded-full border px-2.5 py-0.5 text-[10px] font-medium ${toneBadge(badge.tone ?? 'neutral')}`}
                      >
                        {badge.label}
                      </span>
                    ))}
                  </div>
                  <p className="text-sm font-medium text-foreground">{event.title}</p>
                  {event.description ? (
                    <p className="max-w-4xl text-sm leading-relaxed text-muted-foreground">
                      {event.description}
                    </p>
                  ) : null}
                </div>
                <span className="text-xs text-muted-foreground">
                  {formatDateTime(event.timestamp)}
                </span>
              </div>
            </div>
          </li>
        )
      })}
    </ol>
  )
}

function eventTone(action: string): Tone {
  switch (action) {
    case 'Approved':
      return 'success'
    case 'Denied':
    case 'Deleted':
      return 'danger'
    case 'AutoDenied':
    case 'Expired':
      return 'warning'
    case 'Created':
    case 'Recommendation':
      return 'info'
    case 'Updated':
      return 'neutral'
    default:
      return 'neutral'
  }
}

function eventIcon(action: string) {
  switch (action) {
    case 'Approved':
      return CheckCircle2
    case 'Denied':
      return XCircle
    case 'Deleted':
      return Trash2
    case 'AutoDenied':
    case 'Expired':
      return Clock3
    case 'Created':
      return ShieldAlert
    case 'Recommendation':
      return Sparkles
    case 'Updated':
      return PencilLine
    default:
      return Info
  }
}
