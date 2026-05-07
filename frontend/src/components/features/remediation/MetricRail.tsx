import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

export type MetricRailTone = 'default' | 'danger' | 'success' | 'warning'

export type MetricRailItem = {
  eyebrow: string
  value: ReactNode
  sub?: ReactNode
  tone?: MetricRailTone
  mono?: boolean
  eyebrowPrefix?: ReactNode
  after?: ReactNode
}

type MetricRailProps = {
  items: MetricRailItem[]
  className?: string
}

export function MetricRail({ items, className }: MetricRailProps) {
  return (
    <div
      data-testid="security-workbench-metric-rail"
      className={cn(
        'grid grid-cols-2 border-t border-border/60 pt-4 min-[960px]:grid-cols-3 xl:grid-cols-6',
        className,
      )}
    >
      {items.map((item, index) => (
        <MetricCell key={item.eyebrow} item={item} index={index} />
      ))}
    </div>
  )
}

function MetricCell({ item, index }: { item: MetricRailItem; index: number }) {
  const tone = item.tone ?? 'default'

  return (
    <div
      className={cn(
        'min-w-0 px-3 py-1.5 first:pl-0 xl:px-4',
        index > 0 && 'border-l border-border/60',
        index % 2 === 0 && 'max-[959px]:border-l-0',
        index % 3 === 0 && 'min-[960px]:max-xl:border-l-0',
      )}
    >
      <div
        className={cn(
          'flex min-w-0 items-center gap-1 text-[9.5px] font-bold uppercase leading-none tracking-[0.14em] text-muted-foreground',
          tone === 'danger' && 'text-destructive/80',
        )}
      >
        {item.eyebrowPrefix ? (
          <span aria-hidden="true" className="text-[10px] leading-none">
            {item.eyebrowPrefix}
          </span>
        ) : null}
        <span className="truncate">{item.eyebrow}</span>
      </div>
      <div
        className={cn(
          'mt-1 truncate text-lg font-semibold leading-6 text-foreground',
          item.mono && 'font-mono text-[17px]',
          tone === 'success' && 'text-emerald-600 dark:text-emerald-400',
          tone === 'warning' && 'text-amber-600 dark:text-amber-400',
          tone === 'danger' && 'text-destructive',
        )}
      >
        {item.value}
      </div>
      {item.sub ? (
        <div
          className={cn(
            'mt-0.5 truncate text-[11px] leading-4 text-muted-foreground',
            tone === 'danger' && 'text-destructive/80',
          )}
        >
          {item.sub}
        </div>
      ) : null}
      {item.after ? <div className="mt-1.5 flex min-w-0 flex-wrap gap-1">{item.after}</div> : null}
    </div>
  )
}
