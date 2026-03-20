import type { ReactNode } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { InsetPanel } from '@/components/ui/inset-panel'
import { cn } from '@/lib/utils'

type DataTableWorkbenchProps = {
  title: string
  description?: string
  totalCount?: number
  children: ReactNode
  className?: string
}

type DataTableSummaryItem = {
  label: string
  value: string
  tone?: 'default' | 'accent' | 'warning'
}

type DataTableActiveFilter = {
  key: string
  label: string
  onClear: () => void
}

export function DataTableWorkbench({
  title,
  description,
  totalCount,
  children,
  className,
}: DataTableWorkbenchProps) {
  return (
    <section className={cn('rounded-2xl border border-border/70 bg-card/85 shadow-sm', className)}>
      <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border/60 px-5 py-5">
        <div className="space-y-1.5">
          <div className="flex flex-wrap items-center gap-3">
            <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
            {typeof totalCount === 'number' ? (
              <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
                {totalCount} total
              </Badge>
            ) : null}
          </div>
          {description ? <p className="max-w-3xl text-sm text-muted-foreground">{description}</p> : null}
        </div>
      </div>
      <div className="space-y-4 px-5 py-5">{children}</div>
    </section>
  )
}

export function DataTableToolbar({
  children,
  className,
}: {
  children: ReactNode
  className?: string
}) {
  return (
    <InsetPanel
      emphasis="default"
      className={cn(
        'flex flex-col gap-3 rounded-xl p-4 backdrop-blur-sm',
        className,
      )}
    >
      {children}
    </InsetPanel>
  )
}

export function DataTableToolbarRow({
  children,
  className,
}: {
  children: ReactNode
  className?: string
}) {
  return <div className={cn('flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between', className)}>{children}</div>
}

export function DataTableSummaryStrip({
  items,
  className,
}: {
  items: DataTableSummaryItem[]
  className?: string
}) {
  return (
    <div className={cn('grid gap-3 sm:grid-cols-2 xl:grid-cols-4', className)}>
      {items.map((item) => (
        <div
          key={item.label}
          className={cn(
            'rounded-xl px-4 py-3',
            item.tone === 'accent'
              ? 'border-primary/20 bg-primary/8'
              : item.tone === 'warning'
                ? 'border-tone-warning-border bg-tone-warning'
                : 'border border-border/80 bg-card',
          )}
        >
          <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground">{item.label}</p>
          <p className="mt-2 text-xl font-semibold tracking-tight">{item.value}</p>
        </div>
      ))}
    </div>
  )
}

export function DataTableFilterBar({
  children,
  className,
}: {
  children: ReactNode
  className?: string
}) {
  return (
    <InsetPanel
      emphasis="default"
      className={cn(
        'grid gap-3 rounded-xl p-4 lg:grid-cols-[minmax(0,1.4fr)_repeat(3,minmax(180px,0.8fr))]',
        className,
      )}
    >
      {children}
    </InsetPanel>
  )
}

export function DataTableField({
  label,
  hint,
  children,
  className,
}: {
  label: string
  hint?: string
  children: ReactNode
  className?: string
}) {
  return (
    <label className={cn('flex min-w-0 flex-col gap-2', className)}>
      <span className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground">{label}</span>
      {children}
      {hint ? <span className="text-xs text-muted-foreground">{hint}</span> : null}
    </label>
  )
}

export function DataTableActiveFilters({
  filters,
  onClearAll,
  className,
}: {
  filters: DataTableActiveFilter[]
  onClearAll?: () => void
  className?: string
}) {
  if (filters.length === 0) {
    return null
  }

  return (
    <div className={cn('flex flex-wrap items-center gap-2', className)}>
      <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">Active filters</span>
      {filters.map((filter) => (
        <Badge key={filter.key} variant="outline" className="h-7 gap-2 rounded-full border-border/80 bg-card px-3">
          <span>{filter.label}</span>
          <button
            type="button"
            className="text-muted-foreground transition hover:text-foreground"
            onClick={filter.onClear}
            aria-label={`Clear ${filter.label}`}
          >
            ×
          </button>
        </Badge>
      ))}
      {onClearAll ? (
        <Button type="button" variant="ghost" size="sm" onClick={onClearAll}>
          Clear all
        </Button>
      ) : null}
    </div>
  )
}

export function DataTableEmptyState({
  title,
  description,
}: {
  title: string
  description: string
}) {
  return (
    <InsetPanel className="flex min-h-44 flex-col items-center justify-center rounded-xl border-dashed px-6 py-10 text-center">
      <p className="text-base font-medium tracking-tight">{title}</p>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">{description}</p>
    </InsetPanel>
  )
}
