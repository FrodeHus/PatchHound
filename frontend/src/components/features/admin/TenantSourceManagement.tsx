import { useState } from 'react'
import { useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { ChevronDown, KeyRound, RotateCw } from 'lucide-react'
import { triggerTenantIngestionSync, updateTenant } from '@/api/settings.functions'
import type { TenantDetail, TenantIngestionSource } from '@/api/settings.schemas'
import { SourceRunHistorySheet } from '@/components/features/admin/SourceRunHistorySheet'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

type TenantSourceManagementProps = {
  tenant: TenantDetail
}

export function TenantSourceManagement({ tenant }: TenantSourceManagementProps) {
  const router = useRouter()
  const [sources, setSources] = useState(() => tenant.ingestionSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [syncingSourceKey, setSyncingSourceKey] = useState<string | null>(null)
  const [syncState, setSyncState] = useState<'idle' | 'success' | 'error'>('idle')
  const [expandedSourceKey, setExpandedSourceKey] = useState<string | null>(null)
  const [historySourceKey, setHistorySourceKey] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: async () => {
      await updateTenant({
        data: {
          tenantId: tenant.id,
          name: tenant.name,
          sla: tenant.sla,
          ingestionSources: sources.map((source) => ({
            key: source.key,
            displayName: source.displayName,
            enabled: source.enabled,
            syncSchedule: source.syncSchedule,
            credentials: {
              clientId: source.credentials.clientId,
              secret: source.credentials.secret,
              apiBaseUrl: source.credentials.apiBaseUrl,
              tokenScope: source.credentials.tokenScope,
            },
          })),
        },
      })
    },
    onSuccess: () => {
      setSaveState('saved')
      void router.invalidate()
    },
    onError: () => {
      setSaveState('error')
    },
  })

  const syncMutation = useMutation({
    mutationFn: async (sourceKey: string) => {
      await triggerTenantIngestionSync({
        data: {
          tenantId: tenant.id,
          sourceKey,
        },
      })
    },
    onMutate: (sourceKey) => {
      setSyncingSourceKey(sourceKey)
      setSyncState('idle')
    },
    onSuccess: async () => {
      setSyncState('success')
      await router.invalidate()
    },
    onError: () => {
      setSyncState('error')
    },
    onSettled: () => {
      setSyncingSourceKey(null)
    },
  })

  function updateSource(
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) {
    setSaveState('idle')
    setSources((current) => current.map((source) => (source.key === key ? mutate(source) : source)))
  }

  return (
    <section className="space-y-5">
      <Card className="rounded-[30px] border-border/70 bg-card/82 shadow-sm">
        <CardHeader className="border-b border-border/60 pb-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="space-y-1">
              <h2 className="text-2xl font-semibold tracking-[-0.03em]">Tenant Sources</h2>
              <p className="text-sm text-muted-foreground">
                Configure ingestion connectors, credentials, schedules, and manual sync for the selected tenant.
              </p>
            </div>
          </div>
        </CardHeader>

        <CardContent className="space-y-5 pt-5">
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="rounded-[24px] border border-border/70 bg-background/30 p-4">
              <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Configured Sources</p>
              <p className="mt-2 text-2xl font-semibold">
                {sources.filter((source) => source.credentials.clientId || source.credentials.hasSecret).length}
              </p>
            </div>
            <div className="rounded-[24px] border border-border/70 bg-background/30 p-4">
              <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Enabled Sources</p>
              <p className="mt-2 text-2xl font-semibold">{sources.filter((source) => source.enabled).length}</p>
            </div>
            <div className="rounded-[24px] border border-border/70 bg-background/30 p-4">
              <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Manual Sync</p>
              <p className="mt-2 text-sm text-muted-foreground">
                Supported for {sources.filter((source) => source.supportsManualSync).length} source{sources.filter((source) => source.supportsManualSync).length === 1 ? '' : 's'}.
              </p>
            </div>
          </div>

          <div className="rounded-[26px] border border-border/70 bg-background/25 p-4 sm:p-5">
            <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border/60 pb-4">
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <h3 className="text-lg font-semibold">Tenant Sources</h3>
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
                    {sources.length}
                  </Badge>
                </div>
                <p className="text-sm text-muted-foreground">
                  Credentials and schedules used by the worker to collect tenant inventory and vulnerability data.
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2 text-xs">
                {saveState === 'saved' ? <StatusPill tone="success">Configuration saved</StatusPill> : null}
                {saveState === 'error' ? <StatusPill tone="error">Save failed</StatusPill> : null}
                {syncState === 'success' ? <StatusPill tone="success">Sync queued</StatusPill> : null}
                {syncState === 'error' ? <StatusPill tone="error">Sync failed</StatusPill> : null}
              </div>
            </div>

            <div className="mt-5 space-y-4">
              <SourceSection
                tenant={tenant}
                sources={sources}
                expandedSourceKey={expandedSourceKey}
                syncingSourceKey={syncingSourceKey}
                isSaving={mutation.isPending}
                syncMutation={syncMutation}
                onSave={() => mutation.mutate()}
                onOpenHistory={setHistorySourceKey}
                onToggleExpanded={(sourceKey) => {
                  setExpandedSourceKey((current) => (current === sourceKey ? null : sourceKey))
                }}
                onUpdateSource={updateSource}
              />
            </div>
          </div>
        </CardContent>
      </Card>
      <SourceRunHistorySheet
        tenantId={tenant.id}
        sourceKey={historySourceKey}
        sourceDisplayName={sources.find((source) => source.key === historySourceKey)?.displayName ?? null}
        isOpen={historySourceKey !== null}
        onOpenChange={(open) => {
          if (!open) {
            setHistorySourceKey(null)
          }
        }}
      />
    </section>
  )
}

function StatusPill({
  children,
  tone,
}: {
  children: string
  tone: 'success' | 'error'
}) {
  return (
    <span
      className={cn(
        'rounded-full border px-3 py-1',
        tone === 'success'
          ? 'border-emerald-400/25 bg-emerald-400/10 text-emerald-300'
          : 'border-destructive/25 bg-destructive/10 text-destructive',
      )}
    >
      {children}
    </span>
  )
}

function SourceSection({
  tenant,
  sources,
  expandedSourceKey,
  syncingSourceKey,
  isSaving,
  syncMutation,
  onSave,
  onOpenHistory,
  onToggleExpanded,
  onUpdateSource,
}: {
  tenant: TenantDetail
  sources: TenantIngestionSourceDraft[]
  expandedSourceKey: string | null
  syncingSourceKey: string | null
  isSaving: boolean
  syncMutation: ReturnType<typeof useMutation<void, Error, string>>
  onSave: () => void
  onOpenHistory: (sourceKey: string) => void
  onToggleExpanded: (sourceKey: string) => void
  onUpdateSource: (
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) => void
}) {
  if (!sources.length) {
    return (
      <div className="rounded-[22px] border border-dashed border-border/70 bg-background/20 px-4 py-8 text-sm text-muted-foreground">
        No sources are available in this category for the selected tenant.
      </div>
    )
  }

  return (
    <section className="space-y-4">
      {sources.map((source) => {
        const isConfigured = Boolean(
          source.credentials.clientId || source.credentials.hasSecret,
        )
        const isExpanded = expandedSourceKey === source.key
        const secretLabel = 'Client Secret'
        const statusTone = getSourceStatusTone(source)
        const statusLabel = source.runtime.lastStatus ?? (isConfigured ? 'Configured' : 'Needs credentials')

        return (
          <Card key={source.key} className="rounded-[26px] border-border/70 bg-card/82 shadow-sm">
            <button
              type="button"
              className="flex w-full flex-wrap items-center gap-3 px-5 py-4 text-left transition-colors hover:bg-background/25"
              onClick={() => onToggleExpanded(source.key)}
            >
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
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
                <StatusBadge tone={statusTone}>{statusLabel}</StatusBadge>
                {source.supportsManualSync ? (
                  <Button
                    type="button"
                    variant="outline"
                    className="rounded-full"
                    disabled={syncMutation.isPending || !source.enabled}
                    onClick={(event) => {
                      event.stopPropagation()
                      syncMutation.mutate(source.key)
                    }}
                  >
                    <RotateCw className="size-4" />
                    {syncingSourceKey === source.key ? 'Syncing...' : 'Manual sync'}
                  </Button>
                ) : null}
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
              {source.supportsManualSync ? (
                <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                  <div className="space-y-1">
                    <p className="text-sm font-medium">Manual ingestion sync</p>
                    <p className="text-xs text-muted-foreground">
                      Force a new source sync immediately. Last successful sync: {formatTimestamp(source.runtime.lastSucceededAt)}
                    </p>
                    {source.runtime.lastError ? (
                      <p className="text-xs text-destructive">Last error: {source.runtime.lastError}</p>
                    ) : null}
                  </div>
                  <Button
                    type="button"
                    variant="outline"
                    className="rounded-full"
                    disabled={syncMutation.isPending || !source.enabled}
                    onClick={() => syncMutation.mutate(source.key)}
                  >
                    <RotateCw className="size-4" />
                    {syncingSourceKey === source.key ? 'Syncing...' : 'Run sync now'}
                  </Button>
                </div>
              ) : (
                <div className="flex items-start gap-3 rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                  <KeyRound className="mt-0.5 size-4 text-primary" />
                  <div className="space-y-1">
                    <p className="text-sm font-medium">Worker enrichment</p>
                    <p className="text-xs text-muted-foreground">
                      This source does not run on its own schedule. It is invoked automatically while other vulnerability syncs are being processed.
                    </p>
                    {source.runtime.lastError ? (
                      <p className="text-xs text-destructive">Last error: {source.runtime.lastError}</p>
                    ) : null}
                  </div>
                </div>
              )}

              <label className="flex items-center gap-3 rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                <input
                  type="checkbox"
                  checked={source.enabled}
                  onChange={(event) => {
                    onUpdateSource(source.key, (current) => ({
                      ...current,
                      enabled: event.target.checked,
                    }))
                  }}
                />
                <div>
                  <p className="text-sm font-medium">Enable source</p>
                  <p className="text-xs text-muted-foreground">
                    Included in tenant ingestion schedule and credential validation.
                  </p>
                </div>
              </label>

              <div className="grid gap-4 md:grid-cols-2">
                <label className="space-y-2">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Display Name</span>
                  <Input
                    value={source.displayName}
                    onChange={(event) => {
                      onUpdateSource(source.key, (current) => ({
                        ...current,
                        displayName: event.target.value,
                      }))
                    }}
                  />
                </label>
                {source.supportsScheduling ? (
                  <label className="space-y-2">
                    <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Sync Schedule</span>
                    <Input
                      value={source.syncSchedule}
                      onChange={(event) => {
                        onUpdateSource(source.key, (current) => ({
                          ...current,
                          syncSchedule: event.target.value,
                        }))
                      }}
                    />
                  </label>
                ) : (
                  <div className="rounded-2xl border border-dashed border-border/70 bg-background/25 px-4 py-3 text-sm text-muted-foreground">
                    This source does not currently support a dedicated schedule.
                  </div>
                )}
              </div>

              <div className="grid gap-4 md:grid-cols-2">
                <div className="rounded-2xl border border-dashed border-border/70 bg-background/25 px-4 py-3 text-sm text-muted-foreground md:col-span-2">
                  This source automatically uses the selected tenant&apos;s Entra tenant ID: <span className="font-medium text-foreground">{tenant.entraTenantId}</span>.
                </div>
                <label className="space-y-2">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Client ID</span>
                  <Input
                    value={source.credentials.clientId}
                    onChange={(event) => {
                      onUpdateSource(source.key, (current) => ({
                        ...current,
                        credentials: { ...current.credentials, clientId: event.target.value },
                      }))
                    }}
                  />
                </label>
                <label className="space-y-2 md:col-span-2">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{secretLabel}</span>
                  <Input
                    type="password"
                    value={source.credentials.secret}
                    placeholder={source.credentials.hasSecret ? 'Stored in OpenBao. Enter a new value to rotate.' : 'Not configured'}
                    onChange={(event) => {
                      onUpdateSource(source.key, (current) => ({
                        ...current,
                        credentials: {
                          ...current.credentials,
                          secret: event.target.value,
                          hasSecret: current.credentials.hasSecret || event.target.value.trim().length > 0,
                        },
                      }))
                    }}
                  />
                </label>
                <label className="space-y-2">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">API Base URL</span>
                  <Input
                    value={source.credentials.apiBaseUrl}
                    onChange={(event) => {
                      onUpdateSource(source.key, (current) => ({
                        ...current,
                        credentials: { ...current.credentials, apiBaseUrl: event.target.value },
                      }))
                    }}
                  />
                </label>
                <label className="space-y-2">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Token Scope</span>
                  <Input
                    value={source.credentials.tokenScope}
                    onChange={(event) => {
                      onUpdateSource(source.key, (current) => ({
                        ...current,
                        credentials: { ...current.credentials, tokenScope: event.target.value },
                      }))
                    }}
                  />
                </label>
              </div>

              <div className="space-y-3 rounded-2xl border border-border/70 bg-background/20 px-4 py-4">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-medium">Recent ingestion runs</p>
                    <p className="text-xs text-muted-foreground">
                      Latest worker runs for this source, including staged and merged item counts.
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
                      {source.recentRuns.length}
                    </Badge>
                    <Button
                      type="button"
                      variant="outline"
                      className="rounded-full"
                      onClick={() => onOpenHistory(source.key)}
                    >
                      View full history
                    </Button>
                  </div>
                </div>

                {source.recentRuns.length ? (
                  <div className="space-y-2">
                    {source.recentRuns.map((run) => (
                      <div
                        key={run.id}
                        className="rounded-2xl border border-border/70 bg-background/50 px-4 py-3"
                      >
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div className="flex flex-wrap items-center gap-2 text-sm">
                            <StatusBadge tone={getRunStatusTone(run.status)}>{run.status}</StatusBadge>
                            <span className="text-muted-foreground">
                              Started {formatTimestamp(run.startedAt)}
                            </span>
                            <span className="text-muted-foreground">
                              Completed {formatTimestamp(run.completedAt)}
                            </span>
                          </div>
                          <span className="text-xs text-muted-foreground">{run.id.slice(0, 8)}</span>
                        </div>

                        <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-3 lg:grid-cols-4">
                          <RunMetric label="Fetched Vulns" value={run.fetchedVulnerabilityCount} />
                          <RunMetric label="Fetched Assets" value={run.fetchedAssetCount} />
                          <RunMetric label="Fetched SW Links" value={run.fetchedSoftwareInstallationCount} />
                          <RunMetric label="Staged Vulns" value={run.stagedVulnerabilityCount} />
                          <RunMetric label="Staged Exposures" value={run.stagedExposureCount} />
                          <RunMetric label="Merged Exposures" value={run.mergedExposureCount} />
                          <RunMetric label="Opened Projections" value={run.openedProjectionCount} />
                          <RunMetric label="Resolved Projections" value={run.resolvedProjectionCount} />
                          <RunMetric label="Staged Assets" value={run.stagedAssetCount} />
                          <RunMetric label="Merged Assets" value={run.mergedAssetCount} />
                          <RunMetric label="Staged SW Links" value={run.stagedSoftwareLinkCount} />
                          <RunMetric label="Resolved SW Links" value={run.resolvedSoftwareLinkCount} />
                          <RunMetric label="Installs Created" value={run.installationsCreated} />
                          <RunMetric label="Installs Touched" value={run.installationsTouched} />
                          <RunMetric label="Episodes Opened" value={run.installationEpisodesOpened} />
                          <RunMetric label="Episodes Seen" value={run.installationEpisodesSeen} />
                          <RunMetric label="Stale Installs" value={run.staleInstallationsMarked} />
                          <RunMetric label="Installs Removed" value={run.installationsRemoved} />
                        </div>

                        {run.error ? (
                          <p className="mt-3 text-xs text-destructive">Error: {run.error}</p>
                        ) : null}
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    No ingestion runs have been recorded for this source yet.
                  </p>
                )}
              </div>

              <div className="flex justify-end border-t border-border/60 pt-4">
                <Button onClick={onSave} disabled={isSaving} className="rounded-full px-5">
                  {isSaving ? 'Saving...' : `Save ${source.displayName}`}
                </Button>
              </div>
              </CardContent>
            ) : null}
          </Card>
        )
      })}
    </section>
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
        'rounded-full border px-3 py-1 text-xs',
        tone === 'success' && 'border-emerald-400/25 bg-emerald-400/10 text-emerald-300',
        tone === 'warning' && 'border-amber-400/25 bg-amber-400/10 text-amber-300',
        tone === 'error' && 'border-destructive/25 bg-destructive/10 text-destructive',
        tone === 'neutral' && 'border-border/70 bg-background/60 text-muted-foreground',
      )}
    >
      {children}
    </span>
  )
}

function getSourceStatusTone(source: TenantIngestionSourceDraft): 'neutral' | 'success' | 'warning' | 'error' {
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

function getRunStatusTone(status: string): 'neutral' | 'success' | 'warning' | 'error' {
  const normalized = status.toLowerCase()

  if (normalized === 'succeeded') {
    return 'success'
  }

  if (normalized === 'running') {
    return 'warning'
  }

  if (normalized === 'failed') {
    return 'error'
  }

  return 'neutral'
}

function RunMetric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border/60 bg-background/35 px-3 py-2">
      <p className="text-[11px] uppercase tracking-[0.12em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </div>
  )
}

type TenantIngestionSourceDraft = Omit<TenantIngestionSource, 'credentials'> & {
  credentials: TenantIngestionSource['credentials'] & {
    secret: string
  }
}

function mapSourceToDraft(source: TenantIngestionSource): TenantIngestionSourceDraft {
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

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat('en', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
