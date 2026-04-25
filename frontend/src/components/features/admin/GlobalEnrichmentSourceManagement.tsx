import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Sparkles, X } from 'lucide-react'
import {
  type EnrichmentSource,
  triggerEndOfLifeEnrichment,
  updateEnrichmentSources,
} from '@/server/system.functions'
import { EnrichmentRunHistorySheet } from '@/components/features/admin/EnrichmentRunHistorySheet'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Separator } from '@/components/ui/separator'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { getApiErrorMessage } from '@/lib/api-errors'
import { cn } from '@/lib/utils'

type GlobalEnrichmentSourceManagementProps = {
  sources: EnrichmentSource[]
  onSaved: () => Promise<void> | void
}

type EnrichmentSourceDraft = EnrichmentSource & {
  credentials: EnrichmentSource['credentials'] & { secret: string }
}

export function GlobalEnrichmentSourceManagement({
  sources: initialSources,
  onSaved,
}: GlobalEnrichmentSourceManagementProps) {
  const [sources, setSources] = useState(() => initialSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [editingSourceKey, setEditingSourceKey] = useState<string | null>(null)
  const [historySource, setHistorySource] = useState<{ key: string; displayName: string } | null>(null)
  const { selectedTenantId } = useTenantScope()

  const eolTriggerMutation = useMutation({
    mutationFn: async () => {
      if (!selectedTenantId) throw new Error('No tenant selected')
      return triggerEndOfLifeEnrichment({ data: { tenantId: selectedTenantId } })
    },
    onSuccess: (result) => {
      toast.success(`EOL enrichment triggered for ${result.enqueuedCount} software items`)
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to trigger EOL enrichment'))
    },
  })

  const mutation = useMutation({
    mutationFn: async () => {
      await updateEnrichmentSources({
        data: sources.map((source) => ({
          key: source.key,
          displayName: source.displayName,
          enabled: source.enabled,
          refreshTtlHours: source.refreshTtlHours,
          credentials: {
            secret: source.credentials.secret,
            apiBaseUrl: source.credentials.apiBaseUrl,
          },
        })),
      })
    },
    onSuccess: async () => {
      setSaveState('saved')
      toast.success('Enrichment configuration saved')
      await onSaved()
    },
    onError: (error) => {
      setSaveState('error')
      toast.error(getApiErrorMessage(error, 'Failed to save enrichment configuration'))
    },
  })

  function updateSource(key: string, mutate: (current: EnrichmentSourceDraft) => EnrichmentSourceDraft) {
    setSaveState('idle')
    setSources((current) => current.map((source) => (source.key === key ? mutate(source) : source)))
  }

  const editingSource = sources.find((s) => s.key === editingSourceKey) ?? null

  return (
    <section className="space-y-4">
      <EnrichmentRunHistorySheet
        sourceKey={historySource?.key ?? null}
        sourceDisplayName={historySource?.displayName ?? null}
        isOpen={historySource !== null}
        onOpenChange={(open) => {
          if (!open) setHistorySource(null)
        }}
      />

      <div className="space-y-1">
        <h2 className="text-lg font-semibold">Global Enrichment</h2>
        <p className="text-sm text-muted-foreground">
          Shared enrichment providers used across all tenants during vulnerability processing.
        </p>
      </div>

      {sources.length ? (
        <div className="overflow-hidden rounded-xl border border-border/70">
          <Table>
            <TableHeader>
              <TableRow className="border-border/60 hover:bg-transparent">
                <TableHead className="h-9 pl-4 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  Provider
                </TableHead>
                <TableHead className="h-9 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  Status
                </TableHead>
                <TableHead className="h-9 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  Queue
                </TableHead>
                <TableHead className="h-9 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  Last run
                </TableHead>
                <TableHead className="h-9 pr-4 text-right text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  Actions
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {sources.map((source) => {
                const statusLabel = getProviderStatusLabel(source)
                const statusTone = getProviderStatusTone(source)

                return (
                  <TableRow
                    key={source.key}
                    className={cn(
                      'cursor-pointer border-border/50',
                      editingSourceKey === source.key && 'bg-primary/[0.04]',
                    )}
                    onClick={() => setEditingSourceKey(source.key)}
                  >
                    {/* Provider */}
                    <TableCell className="py-3 pl-4">
                      <div className="flex items-center gap-2">
                        <div className="flex size-6 shrink-0 items-center justify-center rounded-lg border border-primary/20 bg-primary/10 text-primary">
                          <Sparkles className="size-3" />
                        </div>
                        <span className="font-medium text-foreground">{source.displayName}</span>
                        <span className="rounded-full border border-border/60 bg-muted/30 px-2 py-0.5 font-mono text-[11px] text-muted-foreground">
                          {source.key}
                        </span>
                      </div>
                      {source.runtime.lastError ? (
                        <p
                          className="mt-0.5 max-w-[200px] truncate pl-8 text-[11px] text-destructive"
                          title={source.runtime.lastError}
                        >
                          {source.runtime.lastError}
                        </p>
                      ) : null}
                    </TableCell>

                    {/* Status */}
                    <TableCell className="py-3">
                      <StatusBadge tone={statusTone}>{statusLabel}</StatusBadge>
                    </TableCell>

                    {/* Queue */}
                    <TableCell className="py-3">
                      <div className="flex flex-wrap gap-1.5">
                        {source.queue.pendingCount > 0 ? (
                          <QueueChip label="Pending" value={source.queue.pendingCount} tone="warning" />
                        ) : null}
                        {source.queue.runningCount > 0 ? (
                          <QueueChip label="Running" value={source.queue.runningCount} tone="info" />
                        ) : null}
                        {source.queue.failedCount > 0 ? (
                          <QueueChip label="Failed" value={source.queue.failedCount} tone="error" />
                        ) : null}
                        {source.queue.pendingCount === 0 &&
                          source.queue.runningCount === 0 &&
                          source.queue.failedCount === 0 ? (
                          <span className="text-[11px] text-muted-foreground">Idle</span>
                        ) : null}
                      </div>
                    </TableCell>

                    {/* Last run */}
                    <TableCell className="py-3">
                      <p className="text-[13px]">{formatTimestamp(source.runtime.lastCompletedAt)}</p>
                      {source.runtime.lastSucceededAt !== source.runtime.lastCompletedAt ? (
                        <p className="mt-0.5 text-[11px] text-muted-foreground">
                          Last success {formatTimestamp(source.runtime.lastSucceededAt)}
                        </p>
                      ) : null}
                    </TableCell>

                    {/* Actions */}
                    <TableCell className="py-3 pr-4">
                      <div className="flex items-center justify-end gap-1.5">
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={(e) => {
                            e.stopPropagation()
                            setEditingSourceKey(source.key)
                          }}
                        >
                          Edit
                        </Button>
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={(e) => {
                            e.stopPropagation()
                            setHistorySource({ key: source.key, displayName: source.displayName })
                          }}
                        >
                          History
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </div>
      ) : (
        <InsetPanel emphasis="subtle" className="border-dashed px-4 py-8 text-sm text-muted-foreground">
          No enrichment providers are configured.
        </InsetPanel>
      )}

      {/* Editor sheet */}
      <Sheet
        open={editingSource !== null}
        onOpenChange={(open) => {
          if (!open) setEditingSourceKey(null)
        }}
      >
        <SheetContent
          showCloseButton={false}
          className="flex flex-col gap-0 overflow-hidden p-0 sm:max-w-xl"
          side="right"
        >
          {editingSource ? (
            <EnrichmentSourceEditorSheetContent
              source={editingSource}
              isSaving={mutation.isPending}
              saveState={saveState}
              selectedTenantId={selectedTenantId}
              isEolTriggering={eolTriggerMutation.isPending}
              onSave={() => mutation.mutate()}
              onClose={() => setEditingSourceKey(null)}
              onUpdateSource={updateSource}
              onTriggerEol={() => eolTriggerMutation.mutate()}
              onViewHistory={() => {
                setEditingSourceKey(null)
                setHistorySource({ key: editingSource.key, displayName: editingSource.displayName })
              }}
            />
          ) : null}
        </SheetContent>
      </Sheet>
    </section>
  )
}

// ─── Enrichment editor sheet content ────────────────────────────────────────

function EnrichmentSourceEditorSheetContent({
  source,
  isSaving,
  saveState,
  selectedTenantId,
  isEolTriggering,
  onSave,
  onClose,
  onUpdateSource,
  onTriggerEol,
  onViewHistory,
}: {
  source: EnrichmentSourceDraft
  isSaving: boolean
  saveState: 'idle' | 'saved' | 'error'
  selectedTenantId: string | null | undefined
  isEolTriggering: boolean
  onSave: () => void
  onClose: () => void
  onUpdateSource: (key: string, mutate: (current: EnrichmentSourceDraft) => EnrichmentSourceDraft) => void
  onTriggerEol: () => void
  onViewHistory: () => void
}) {
  return (
    <>
      <SheetHeader className="shrink-0 border-b border-border/60 p-5">
        <div className="flex items-start justify-between gap-3 pr-1">
          <div>
            <SheetTitle>Edit {source.displayName}</SheetTitle>
            <SheetDescription className="mt-1">
              Configure this enrichment provider's credentials and behavior.
            </SheetDescription>
          </div>
          <Button type="button" variant="ghost" size="icon-sm" onClick={onClose} className="-mr-1 -mt-1 shrink-0">
            <X className="size-4" />
          </Button>
        </div>
      </SheetHeader>

      <div className="flex-1 overflow-y-auto">
        {/* Status / posture */}
        <div className="space-y-3 border-b border-border/60 p-5">
          <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">Provider posture</p>
          <div className="divide-y divide-border/50 overflow-hidden rounded-lg border border-border/60">
            <PostureRow label="Status" value={getProviderStatusLabel(source)} />
            <PostureRow label="Last run" value={formatTimestamp(source.runtime.lastCompletedAt)} />
            <PostureRow label="Last success" value={formatTimestamp(source.runtime.lastSucceededAt)} />
          </div>
          <p className="text-xs text-muted-foreground">{getProviderStatusDescription(source)}</p>
          {source.runtime.lastError ? (
            <p className="text-xs text-destructive">Error: {source.runtime.lastError}</p>
          ) : null}

          {/* Queue metrics */}
          <div className="grid grid-cols-4 gap-2">
            <QueueMetricBlock label="Pending" value={source.queue.pendingCount} tone="warning" />
            <QueueMetricBlock label="Retry" value={source.queue.retryScheduledCount} tone="warning" />
            <QueueMetricBlock label="Running" value={source.queue.runningCount} tone="info" />
            <QueueMetricBlock
              label="Failed"
              value={source.queue.failedCount}
              tone={source.queue.failedCount > 0 ? 'error' : 'neutral'}
            />
          </div>

          <Button type="button" variant="outline" size="sm" className="w-full" onClick={onViewHistory}>
            View full run history
          </Button>
        </div>

        {/* EOL manual trigger (endoflife source only) */}
        {source.key === 'endoflife' && source.enabled ? (
          <div className="border-b border-border/60 p-5">
            <div className="flex items-center justify-between gap-4 rounded-lg border border-border/60 px-4 py-3">
              <div className="space-y-0.5">
                <p className="text-sm font-medium">Manual sync</p>
                <p className="text-xs text-muted-foreground">
                  Queue end-of-life enrichment for all software in the current tenant.
                </p>
              </div>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={isEolTriggering || !selectedTenantId}
                onClick={onTriggerEol}
              >
                {isEolTriggering ? 'Triggering…' : 'Trigger EOL sync'}
              </Button>
            </div>
          </div>
        ) : null}

        {/* Form */}
        <div className="space-y-6 p-5">
          <FormSection title="Runtime control">
            <label className="flex cursor-pointer items-center justify-between gap-3 rounded-lg border border-border/60 px-4 py-3 transition-colors hover:bg-muted/20">
              <div className="space-y-0.5">
                <p className="text-sm font-medium">Enable provider</p>
                <p className="text-xs text-muted-foreground">
                  When enabled, the worker will invoke this enrichment source during vulnerability processing.
                </p>
              </div>
              <input
                type="checkbox"
                checked={source.enabled}
                onChange={(event) =>
                  onUpdateSource(source.key, (current) => ({
                    ...current,
                    enabled: event.target.checked,
                  }))
                }
              />
            </label>
          </FormSection>

          <FormSection title="Configuration">
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="grid content-start gap-2">
                <span className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">Display Name</span>
                <Input
                  value={source.displayName}
                  onChange={(event) =>
                    onUpdateSource(source.key, (current) => ({
                      ...current,
                      displayName: event.target.value,
                    }))
                  }
                  className="h-10"
                />
              </div>
              <div className="grid content-start gap-2">
                <span className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">API Base URL</span>
                <Input
                  value={source.credentials.apiBaseUrl}
                  onChange={(event) =>
                    onUpdateSource(source.key, (current) => ({
                      ...current,
                      credentials: { ...current.credentials, apiBaseUrl: event.target.value },
                    }))
                  }
                  className="h-10"
                />
              </div>
              {source.key === 'microsoft-defender' ? (
                <div className="grid content-start gap-2 sm:col-span-2">
                  <span className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                    Refresh TTL (hours)
                  </span>
                  <Input
                    type="number"
                    min={1}
                    step={1}
                    value={source.refreshTtlHours ?? ''}
                    onChange={(event) =>
                      onUpdateSource(source.key, (current) => ({
                        ...current,
                        refreshTtlHours:
                          event.target.value.trim() === ''
                            ? null
                            : Math.max(1, Number.parseInt(event.target.value, 10) || 1),
                      }))
                    }
                    className="h-10"
                  />
                  <p className="text-[11px] text-muted-foreground">
                    Defender CVE detail is refreshed asynchronously when stored detail is older than this threshold.
                  </p>
                </div>
              ) : null}
            </div>
          </FormSection>

          <FormSection title="Credentials">
            {source.credentialMode === 'global-secret' || source.key === 'nvd' ? (
              <div className="grid content-start gap-2">
                <span className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                  API Key{source.key === 'nvd' ? ' (optional)' : ''}
                </span>
                <Input
                  type="password"
                  placeholder={
                    source.credentials.hasSecret
                      ? 'API key stored — enter a new key to replace it'
                      : source.key === 'nvd'
                        ? 'Enter NVD API key'
                        : 'Enter API key'
                  }
                  value={source.credentials.secret}
                  onChange={(event) =>
                    onUpdateSource(source.key, (current) => ({
                      ...current,
                      credentials: { ...current.credentials, secret: event.target.value },
                    }))
                  }
                  className="h-10"
                />
                {source.key === 'nvd' ? (
                  <p className="text-[11px] text-muted-foreground">
                    NVD can run without a key, but a key raises the allowed request rate for modified-feed sync.
                  </p>
                ) : null}
              </div>
            ) : source.credentialMode === 'no-credential' ? (
              <div className="rounded-lg border border-border/50 bg-muted/20 px-4 py-3">
                <p className="text-sm font-medium">No credentials required</p>
                <p className="mt-1.5 text-xs text-muted-foreground">
                  This provider uses a public API and does not require any API key or authentication.
                </p>
              </div>
            ) : (
              <div className="rounded-lg border border-border/50 bg-muted/20 px-4 py-3">
                <p className="text-sm font-medium">Credential source</p>
                <p className="mt-1.5 text-xs text-muted-foreground">
                  This provider reuses each tenant's Microsoft Defender credentials from the tenant source
                  configuration in OpenBao. No global API key is required here.
                </p>
              </div>
            )}
          </FormSection>
        </div>
      </div>

      <SheetFooter className="mt-0 shrink-0 flex-row items-center justify-between gap-3 border-t border-border/60 p-5">
        <div className="text-xs">
          {saveState === 'saved' ? (
            <span className="text-tone-success-foreground">Configuration saved</span>
          ) : null}
          {saveState === 'error' ? (
            <span className="text-destructive">Save failed — review and retry</span>
          ) : null}
        </div>
        <div className="flex gap-2">
          <Button type="button" variant="outline" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onSave} disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Save enrichment'}
          </Button>
        </div>
      </SheetFooter>
    </>
  )
}

// ─── Small components ────────────────────────────────────────────────────────

function QueueChip({
  label,
  value,
  tone,
}: {
  label: string
  value: number
  tone: 'warning' | 'info' | 'error'
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-medium',
        tone === 'warning' && 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
        tone === 'info' && 'border-primary/20 bg-primary/10 text-primary',
        tone === 'error' && 'border-destructive/25 bg-destructive/10 text-destructive',
      )}
    >
      {value} {label}
    </span>
  )
}

function QueueMetricBlock({
  label,
  value,
  tone,
}: {
  label: string
  value: number
  tone: 'neutral' | 'warning' | 'error' | 'info'
}) {
  return (
    <div
      className={cn(
        'rounded-lg border px-2 py-2 text-center',
        tone === 'warning' && 'border-tone-warning-border bg-tone-warning',
        tone === 'error' && 'border-destructive/25 bg-destructive/10',
        tone === 'info' && 'border-primary/20 bg-primary/10',
        tone === 'neutral' && 'border-border/60 bg-muted/20',
      )}
    >
      <p className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-semibold text-foreground">{value}</p>
    </div>
  )
}

function PostureRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4 px-3 py-2.5">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-xs font-medium text-foreground">{value}</span>
    </div>
  )
}

function StatusBadge({
  children,
  tone,
}: {
  children: string
  tone: 'neutral' | 'success' | 'warning' | 'error'
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-[11px] font-medium',
        tone === 'success' && 'border-tone-success-border bg-tone-success text-tone-success-foreground',
        tone === 'warning' && 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
        tone === 'error' && 'border-destructive/25 bg-destructive/10 text-destructive',
        tone === 'neutral' && 'border-border/70 bg-background/50 text-muted-foreground',
      )}
    >
      <span
        className={cn(
          'size-1.5 rounded-full',
          tone === 'success' && 'bg-tone-success-foreground',
          tone === 'warning' && 'bg-tone-warning-foreground',
          tone === 'error' && 'bg-destructive',
          tone === 'neutral' && 'bg-muted-foreground',
        )}
      />
      {children}
    </span>
  )
}

function FormSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="space-y-4">
      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">{title}</p>
      {children}
      <Separator className="opacity-50" />
    </div>
  )
}

// ─── Logic helpers ──────────────────────────────────────────────────────────

function mapSourceToDraft(source: EnrichmentSource): EnrichmentSourceDraft {
  return {
    ...source,
    credentials: { ...source.credentials, secret: '' },
  }
}

function formatTimestamp(value: string | null) {
  if (!value) return 'Never'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function getProviderStatusLabel(source: EnrichmentSource) {
  if (!source.enabled) return 'Inactive'
  if (source.credentialMode === 'global-secret' && !source.credentials.hasSecret) return 'Needs credentials'
  if (source.runtime.lastError) return 'Needs attention'
  if (source.runtime.lastStatus?.toLowerCase() === 'running') return 'Running'
  if (source.runtime.lastSucceededAt) return 'Healthy'
  return 'Ready'
}

function getProviderStatusTone(source: EnrichmentSource): 'neutral' | 'success' | 'warning' | 'error' {
  if (source.runtime.lastError) return 'error'
  if (!source.enabled) return 'neutral'
  if (source.credentialMode === 'global-secret' && !source.credentials.hasSecret) return 'warning'
  if (source.runtime.lastStatus?.toLowerCase() === 'running') return 'warning'
  if (source.runtime.lastSucceededAt) return 'success'
  return 'neutral'
}

function getProviderStatusDescription(source: EnrichmentSource) {
  if (source.key === 'nvd') {
    if (!source.enabled) return 'NVD enrichment is configured globally but currently inactive.'
    if (!source.credentials.hasSecret) return 'NVD enrichment can run without an API key; add one to increase the allowed request rate for modified-feed sync.'
    if (source.runtime.lastError) return 'The worker is attempting NVD enrichment, but the latest run failed and should be reviewed.'
    return 'NVD is the global backfill source for missing vulnerability metadata when tenant ingestion does not provide it.'
  }

  if (source.key === 'microsoft-defender') {
    if (!source.enabled) return 'Defender enrichment is configured globally but currently inactive.'
    return `Defender CVE detail is refreshed asynchronously after ${source.refreshTtlHours ?? 24} hour(s), instead of being refetched during every ingestion run.`
  }

  if (source.key === 'endoflife') {
    if (!source.enabled) return 'End-of-life enrichment is configured but currently inactive.'
    return 'Enriches software items with end-of-life date and support status from the endoflife.date database.'
  }

  return source.enabled
    ? 'This shared provider is available to enrich tenant vulnerability data during worker processing.'
    : 'This shared provider is configured but not currently used by the worker.'
}
