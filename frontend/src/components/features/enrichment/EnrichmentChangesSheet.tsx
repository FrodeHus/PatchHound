import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { History } from 'lucide-react'
import { fetchEnrichmentChanges } from '@/api/enrichment-changes.functions'
import type { EnrichmentChange, EnrichmentChangeJsonValue } from '@/api/enrichment-changes.schemas'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { formatDateTime } from '@/lib/formatting'

type EnrichmentChangesSheetProps = {
  entityType: string
  entityId: string
  entityLabel: string
  triggerLabel?: string
}

export function EnrichmentChangesSheet({
  entityType,
  entityId,
  entityLabel,
  triggerLabel = 'Enrichment changes',
}: EnrichmentChangesSheetProps) {
  const [open, setOpen] = useState(false)
  const queryKey = useMemo(
    () => ['enrichment-changes', entityType, entityId] as const,
    [entityType, entityId],
  )

  const changesQuery = useQuery({
    queryKey,
    queryFn: () => fetchEnrichmentChanges({ data: { entityType, entityId, page: 1, pageSize: 30 } }),
    enabled: open,
  })

  const changes = changesQuery.data?.items ?? []
  const totalCount = changesQuery.data?.totalCount ?? changes.length

  return (
    <>
      <Button type="button" variant="ghost" size="sm" className="gap-1.5" onClick={() => setOpen(true)}>
        <History className="size-4" />
        {triggerLabel}
      </Button>

      <Sheet open={open} onOpenChange={setOpen}>
        <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-card p-0 sm:max-w-2xl">
          <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
            <SheetTitle>Enrichment changes</SheetTitle>
            <SheetDescription>Field changes recorded for {entityLabel}.</SheetDescription>
          </SheetHeader>

          <div className="space-y-4 p-5">
            <div className="flex items-center justify-between gap-3 border-b border-border/60 pb-3">
              <div>
                <p className="text-sm font-medium text-foreground">Change ledger</p>
                <p className="text-xs text-muted-foreground">Most recent enrichment writes first.</p>
              </div>
              <span className="shrink-0 text-xs text-muted-foreground">
                {changesQuery.isFetching ? 'Loading...' : `${totalCount} changes`}
              </span>
            </div>

            {changesQuery.isError ? (
              <div className="rounded-lg border border-destructive/30 bg-destructive/10 p-4 text-sm text-destructive">
                Enrichment changes could not be loaded.
              </div>
            ) : changes.length === 0 && !changesQuery.isFetching ? (
              <div className="rounded-lg border border-dashed border-border/60 bg-background/35 p-4 text-sm text-muted-foreground">
                No enrichment changes have been recorded for this entity.
              </div>
            ) : (
              <div className="space-y-3">
                {changes.map((change) => (
                  <EnrichmentChangeRow key={change.id} change={change} />
                ))}
              </div>
            )}
          </div>
        </SheetContent>
      </Sheet>
    </>
  )
}

function EnrichmentChangeRow({ change }: { change: EnrichmentChange }) {
  return (
    <article className="rounded-lg border border-border/60 bg-background/35 p-4" aria-label={`${change.displayName} changed`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 space-y-1">
          <p className="break-words text-sm font-medium text-foreground">{change.displayName}</p>
          <p className="text-xs text-muted-foreground">
            {change.sourceDisplayName ?? change.sourceKey} · {formatDateTime(change.changedAt)}
          </p>
        </div>
        <span className="rounded-md border border-border/60 bg-card px-2 py-1 text-xs text-muted-foreground">
          {change.scope}
        </span>
      </div>

      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <ValuePanel label="Old value" value={change.oldValue} />
        <ValuePanel label="New value" value={change.newValue} emphasized />
      </div>

      {change.changeReason ? (
        <p className="mt-3 text-xs text-muted-foreground">{change.changeReason}</p>
      ) : null}
    </article>
  )
}

function ValuePanel({
  label,
  value,
  emphasized = false,
}: {
  label: string
  value: EnrichmentChangeJsonValue
  emphasized?: boolean
}) {
  return (
    <div className={emphasized ? 'rounded-lg border border-primary/25 bg-primary/5 p-3' : 'rounded-lg border border-border/55 bg-card/45 p-3'}>
      <p className="text-[0.7rem] font-medium uppercase text-muted-foreground">{label}</p>
      <pre className="mt-2 max-h-40 overflow-auto whitespace-pre-wrap break-words font-mono text-xs leading-5 text-foreground">
        {formatChangeValue(value)}
      </pre>
    </div>
  )
}

function formatChangeValue(value: EnrichmentChangeJsonValue) {
  if (value === null) {
    return '-'
  }

  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }

  return JSON.stringify(value, null, 2)
}
