import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { ChevronDown, Sparkles } from 'lucide-react'
import { type EnrichmentSource, updateEnrichmentSources } from '@/server/system.functions'
import { EnrichmentRunHistorySheet } from '@/components/features/admin/EnrichmentRunHistorySheet'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

type GlobalEnrichmentSourceManagementProps = {
  sources: EnrichmentSource[]
  onSaved: () => Promise<void> | void
}

export function GlobalEnrichmentSourceManagement({
  sources: initialSources,
  onSaved,
}: GlobalEnrichmentSourceManagementProps) {
  const [sources, setSources] = useState(() => initialSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [expandedSourceKey, setExpandedSourceKey] = useState<string | null>(null)
  const [historySource, setHistorySource] = useState<{ key: string; displayName: string } | null>(null)

  const mutation = useMutation({
    mutationFn: async () => {
      await updateEnrichmentSources({
        data: sources.map((source) => ({
          key: source.key,
          displayName: source.displayName,
          enabled: source.enabled,
          credentials: {
            secret: source.credentials.secret,
            apiBaseUrl: source.credentials.apiBaseUrl,
          },
        })),
      })
    },
    onSuccess: async () => {
      setSaveState('saved')
      await onSaved()
    },
    onError: () => {
      setSaveState('error')
    },
  })

  function updateSource(
    key: string,
    mutate: (current: EnrichmentSourceDraft) => EnrichmentSourceDraft,
  ) {
    setSaveState('idle')
    setSources((current) => current.map((source) => (source.key === key ? mutate(source) : source)))
  }

  return (
    <section className="space-y-5">
      <EnrichmentRunHistorySheet
        sourceKey={historySource?.key ?? null}
        sourceDisplayName={historySource?.displayName ?? null}
        isOpen={historySource !== null}
        onOpenChange={(open) => {
          if (!open) {
            setHistorySource(null)
          }
        }}
      />

      <Card className="rounded-2xl border-border/70 bg-card/82 shadow-sm">
        <CardHeader className="border-b border-border/60 pb-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="space-y-1">
              <h2 className="text-2xl font-semibold tracking-[-0.03em]">Global Enrichment</h2>
              <p className="text-sm text-muted-foreground">
                Configure shared enrichment providers used across all tenants during vulnerability processing.
              </p>
            </div>
            <Button onClick={() => mutation.mutate()} disabled={mutation.isPending} className="rounded-full px-5">
              {mutation.isPending ? 'Saving...' : 'Save enrichment changes'}
            </Button>
          </div>
        </CardHeader>

        <CardContent className="space-y-5 pt-5">
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="rounded-[24px] border border-border/70 bg-background/30 p-4">
              <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Providers</p>
              <p className="mt-2 text-2xl font-semibold">{sources.length}</p>
            </div>
            <div className="rounded-[24px] border border-border/70 bg-background/30 p-4">
              <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Enabled</p>
              <p className="mt-2 text-2xl font-semibold">{sources.filter((source) => source.enabled).length}</p>
            </div>
            <div className="rounded-[24px] border border-border/70 bg-background/30 p-4">
              <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Configured</p>
              <p className="mt-2 text-2xl font-semibold">
                {sources.filter((source) => source.credentials.hasSecret).length}
              </p>
            </div>
          </div>

          <div className="grid gap-3 lg:grid-cols-3">
            {sources.map((source) => (
              <div key={`${source.key}-status`} className="rounded-[24px] border border-border/70 bg-background/30 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{source.displayName}</p>
                    <p className="mt-2 text-sm font-medium">{getProviderStatusLabel(source)}</p>
                  </div>
                  <Badge className={getProviderStatusBadgeClassName(source)}>
                    {source.enabled ? 'Enabled' : 'Disabled'}
                  </Badge>
                </div>
                <p className="mt-3 text-sm leading-6 text-muted-foreground">{getProviderStatusDescription(source)}</p>
              </div>
            ))}
          </div>

          <div className="rounded-[26px] border border-border/70 bg-background/25 p-4 sm:p-5">
            <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border/60 pb-4">
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <h3 className="text-lg font-semibold">Enrichment Providers</h3>
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
                    {sources.length}
                  </Badge>
                </div>
                <p className="text-sm text-muted-foreground">
                  These providers are not tenant-specific. The worker invokes them while processing vulnerability ingestion.
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2 text-xs">
                {saveState === 'saved' ? <p className="rounded-full border border-tone-success-border bg-tone-success px-3 py-1 text-tone-success-foreground">Configuration saved</p> : null}
                {saveState === 'error' ? <p className="rounded-full border border-destructive/25 bg-destructive/10 px-3 py-1 text-destructive">Save failed</p> : null}
              </div>
            </div>

            <div className="mt-5 space-y-4">
              {sources.map((source) => {
                const isExpanded = expandedSourceKey === source.key

                return (
                  <Card key={source.key} className="rounded-[26px] border-border/70 bg-card/82 shadow-sm">
                    <button
                      type="button"
                      className="flex w-full flex-wrap items-center gap-3 px-5 py-4 text-left transition-colors hover:bg-background/25"
                      onClick={() => {
                        setExpandedSourceKey((current) => (current === source.key ? null : source.key))
                      }}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <div className="flex size-8 items-center justify-center rounded-2xl border border-primary/20 bg-primary/10 text-primary">
                            <Sparkles className="size-4" />
                          </div>
                          <CardTitle className="text-base">{source.displayName}</CardTitle>
                          <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                            {source.key}
                          </Badge>
                        </div>
                      </div>
                      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                        <span className="rounded-full border border-border/70 bg-background/60 px-3 py-1">
                          Last run {formatTimestamp(source.runtime.lastCompletedAt)}
                        </span>
                        <StatusBadge tone={getProviderStatusTone(source)}>
                          {getProviderStatusLabel(source)}
                        </StatusBadge>
                        <span
                          className={cn(
                            'inline-flex size-8 items-center justify-center rounded-full border border-border/70 bg-background/60 transition-transform',
                            isExpanded ? 'rotate-180' : '',
                          )}
                        >
                          <ChevronDown className="size-4" />
                        </span>
                      </div>
                    </button>

                    {isExpanded ? (
                      <CardContent className="space-y-5 border-t border-border/60 pt-5">
                        <label className="flex items-center gap-3 rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                          <input
                            type="checkbox"
                            checked={source.enabled}
                            onChange={(event) =>
                              updateSource(source.key, (current) => ({
                                ...current,
                                enabled: event.target.checked,
                              }))
                            }
                          />
                          <div>
                            <p className="text-sm font-medium">Enable provider</p>
                            <p className="text-xs text-muted-foreground">
                              When enabled, the worker will invoke this enrichment source during vulnerability processing.
                            </p>
                          </div>
                        </label>

                        <div className="rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                          <p className="text-sm font-medium">{getProviderStatusDescription(source)}</p>
                          <p className="mt-2 text-xs text-muted-foreground">
                            Last success: {formatTimestamp(source.runtime.lastSucceededAt)}
                          </p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-5">
                          <QueueMetric label="Pending" value={source.queue.pendingCount} tone="warning" />
                          <QueueMetric label="Retry" value={source.queue.retryScheduledCount} tone="warning" />
                          <QueueMetric label="Running" value={source.queue.runningCount} tone="info" />
                          <QueueMetric label="Failed" value={source.queue.failedCount} tone={source.queue.failedCount > 0 ? 'error' : 'neutral'} />
                          <QueueMetric label="Oldest Due" value={formatTimestamp(source.queue.oldestPendingAt)} tone="neutral" />
                        </div>

                        <div className="rounded-2xl border border-border/70 bg-background/20 p-4">
                          <div className="flex flex-wrap items-center justify-between gap-3">
                            <div>
                              <p className="text-sm font-medium">Recent enrichment runs</p>
                              <p className="text-xs text-muted-foreground">
                                Latest queue-processing outcomes for this provider.
                              </p>
                            </div>
                            <Button
                              type="button"
                              variant="outline"
                              className="rounded-full"
                              onClick={() => setHistorySource({ key: source.key, displayName: source.displayName })}
                            >
                              View full history
                            </Button>
                          </div>

                          {source.recentRuns.length ? (
                            <div className="mt-4 space-y-2">
                              {source.recentRuns.map((run) => (
                                <div
                                  key={run.id}
                                  className="grid gap-2 rounded-2xl border border-border/60 bg-background/35 px-4 py-3 text-xs text-muted-foreground sm:grid-cols-[minmax(0,1fr)_auto_auto_auto_auto]"
                                >
                                  <div>
                                    <p className="font-medium text-foreground">{run.status}</p>
                                    <p>Started {formatTimestamp(run.startedAt)}</p>
                                  </div>
                                  <RunStat label="Claimed" value={run.jobsClaimed} />
                                  <RunStat label="Succeeded" value={run.jobsSucceeded} />
                                  <RunStat label="No Data" value={run.jobsNoData} />
                                  <RunStat label="Failed" value={run.jobsFailed} />
                                </div>
                              ))}
                            </div>
                          ) : (
                            <p className="mt-4 text-xs text-muted-foreground">
                              No enrichment runs have been recorded yet.
                            </p>
                          )}
                        </div>

                        <div className="grid gap-4 md:grid-cols-2">
                          <label className="space-y-2">
                            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Display Name</span>
                            <Input
                              value={source.displayName}
                              onChange={(event) =>
                                updateSource(source.key, (current) => ({
                                  ...current,
                                  displayName: event.target.value,
                                }))
                              }
                            />
                          </label>
                          <label className="space-y-2">
                            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">API Base URL</span>
                            <Input
                              value={source.credentials.apiBaseUrl}
                              onChange={(event) =>
                                updateSource(source.key, (current) => ({
                                  ...current,
                                  credentials: {
                                    ...current.credentials,
                                    apiBaseUrl: event.target.value,
                                  },
                                }))
                              }
                            />
                          </label>
                        </div>

                        <label className="space-y-2">
                          <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">API Key</span>
                          <Input
                            type="password"
                            placeholder={source.credentials.hasSecret ? 'API key stored. Enter a new key to replace it.' : 'Enter API key'}
                            value={source.credentials.secret}
                            onChange={(event) =>
                              updateSource(source.key, (current) => ({
                                ...current,
                                credentials: { ...current.credentials, secret: event.target.value },
                              }))
                            }
                          />
                        </label>

                        {source.runtime.lastError ? (
                          <div className="rounded-2xl border border-destructive/25 bg-destructive/8 px-4 py-3 text-xs text-destructive">
                            Last error: {source.runtime.lastError}
                          </div>
                        ) : null}
                      </CardContent>
                    ) : null}
                  </Card>
                )
              })}
            </div>
          </div>
        </CardContent>
      </Card>
    </section>
  )
}

function QueueMetric({
  label,
  value,
  tone,
}: {
  label: string
  value: string | number
  tone: 'neutral' | 'warning' | 'error' | 'info'
}) {
  return (
    <div
      className={cn(
        'rounded-2xl border px-4 py-3',
        tone === 'warning' && 'border-tone-warning-border bg-tone-warning',
        tone === 'error' && 'border-destructive/25 bg-destructive/10',
        tone === 'info' && 'border-primary/20 bg-primary/10',
        tone === 'neutral' && 'border-border/70 bg-background/35',
      )}
    >
      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-sm font-semibold text-foreground">{value}</p>
    </div>
  )
}

function RunStat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border/60 bg-background/30 px-3 py-2 text-center">
      <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </div>
  )
}

type EnrichmentSourceDraft = EnrichmentSource & {
  credentials: EnrichmentSource['credentials'] & { secret: string }
}

function mapSourceToDraft(source: EnrichmentSource): EnrichmentSourceDraft {
  return {
    ...source,
    credentials: {
      ...source.credentials,
      secret: '',
    },
  }
}

function formatTimestamp(value: string | null) {
  if (!value) {
    return 'Never'
  }

  return new Date(value).toLocaleString()
}

function getProviderStatusLabel(source: EnrichmentSource) {
  if (!source.enabled) {
    return 'Inactive'
  }

  if (!source.credentials.hasSecret) {
    return 'Needs credentials'
  }

  if (source.runtime.lastError) {
    return 'Needs attention'
  }

  if (source.runtime.lastStatus?.toLowerCase() === 'running') {
    return 'Running'
  }

  if (source.runtime.lastSucceededAt) {
    return 'Healthy'
  }

  return 'Ready for first run'
}

function getProviderStatusDescription(source: EnrichmentSource) {
  if (source.key === 'nvd') {
    if (!source.enabled) {
      return 'NVD enrichment is configured globally but currently inactive for worker processing.'
    }

    if (!source.credentials.hasSecret) {
      return 'Add an NVD API key so the worker can enrich missing description, published date, CVSS score, and vector data.'
    }

    if (source.runtime.lastError) {
      return 'The worker is attempting NVD enrichment, but the latest run failed and should be reviewed.'
    }

    return 'NVD is the global backfill source for missing vulnerability metadata when tenant ingestion does not provide it.'
  }

  return source.enabled
    ? 'This shared provider is available to enrich tenant vulnerability data during worker processing.'
    : 'This shared provider is configured but not currently used by the worker.'
}

function getProviderStatusBadgeClassName(source: EnrichmentSource) {
  if (!source.enabled) {
    return 'rounded-full border border-border/70 bg-background/70 text-muted-foreground hover:bg-background/70'
  }

  if (!source.credentials.hasSecret || source.runtime.lastError) {
    return 'rounded-full border border-tone-warning-border bg-tone-warning text-tone-warning-foreground hover:bg-tone-warning'
  }

  return 'rounded-full border border-tone-success-border bg-tone-success text-tone-success-foreground hover:bg-tone-success'
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
        'rounded-full border px-3 py-1 text-xs',
        tone === 'success' && 'border-tone-success-border bg-tone-success text-tone-success-foreground',
        tone === 'warning' && 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
        tone === 'error' && 'border-destructive/25 bg-destructive/10 text-destructive',
        tone === 'neutral' && 'border-border/70 bg-background/60 text-muted-foreground',
      )}
    >
      {children}
    </span>
  )
}

function getProviderStatusTone(source: EnrichmentSource): 'neutral' | 'success' | 'warning' | 'error' {
  if (source.runtime.lastError) {
    return 'error'
  }

  if (source.runtime.lastStatus?.toLowerCase() === 'running') {
    return 'warning'
  }

  if (source.runtime.lastSucceededAt) {
    return 'success'
  }

  return 'neutral'
}
