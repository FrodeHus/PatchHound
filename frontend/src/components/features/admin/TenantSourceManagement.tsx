import { useMemo, useState } from 'react'
import { Link, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { ArrowLeft, CircleHelp, PenSquare, RotateCw } from 'lucide-react'
import { triggerTenantIngestionSync, updateTenant } from '@/api/settings.functions'
import type { TenantDetail, TenantIngestionSource } from '@/api/settings.schemas'
import { SourceRunHistorySheet } from '@/components/features/admin/SourceRunHistorySheet'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Separator } from '@/components/ui/separator'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

type TenantSourceManagementProps = {
  tenant: TenantDetail
  editingSourceKey: string | null
  onEditSource: (sourceKey: string) => void
  onCloseEditor: () => void
}

type TenantIngestionSourceDraft = Omit<TenantIngestionSource, 'credentials'> & {
  credentials: TenantIngestionSource['credentials'] & {
    secret: string
  }
}

export function TenantSourceManagement({
  tenant,
  editingSourceKey,
  onEditSource,
  onCloseEditor,
}: TenantSourceManagementProps) {
  const router = useRouter()
  const [sources, setSources] = useState(() => tenant.ingestionSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [syncingSourceKey, setSyncingSourceKey] = useState<string | null>(null)
  const [syncState, setSyncState] = useState<'idle' | 'success' | 'error'>('idle')
  const [historySourceKey, setHistorySourceKey] = useState<string | null>(null)

  const editingSource = useMemo(
    () => sources.find((source) => source.key === editingSourceKey) ?? null,
    [editingSourceKey, sources],
  )

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
    onSuccess: async () => {
      setSaveState('saved')
      await router.invalidate()
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
    <TooltipProvider>
      <section className="space-y-5">
        {editingSource ? (
          <TenantSourceEditorPage
            tenant={tenant}
            source={editingSource}
            isSaving={mutation.isPending}
            saveState={saveState}
            onBack={onCloseEditor}
            onSave={() => mutation.mutate()}
            onUpdateSource={updateSource}
          />
        ) : null}

        {!editingSource ? (
          <>
        <Card className="rounded-2xl bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_94%,black),var(--card))] shadow-sm">
          <CardHeader className="border-b border-border/60 pb-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="space-y-1">
                <h2 className="text-2xl font-semibold tracking-[-0.03em]">Tenant Sources</h2>
                <p className="text-sm text-muted-foreground">
                  Review source status, run manual sync, and edit credentials or schedules in a side panel.
                </p>
              </div>
            </div>
          </CardHeader>

          <CardContent className="space-y-5 pt-5">
            <div className="grid gap-3 sm:grid-cols-3">
              <InsetPanel className="p-4">
                <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Configured Sources</p>
                <p className="mt-2 text-2xl font-semibold">
                  {sources.filter((source) => source.credentials.clientId || source.credentials.hasSecret).length}
                </p>
              </InsetPanel>
              <InsetPanel className="p-4">
                <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Enabled Sources</p>
                <p className="mt-2 text-2xl font-semibold">{sources.filter((source) => source.enabled).length}</p>
              </InsetPanel>
              <InsetPanel className="p-4">
                <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Manual Sync</p>
                <p className="mt-2 text-sm text-muted-foreground">
                  Supported for {sources.filter((source) => source.supportsManualSync).length} source{sources.filter((source) => source.supportsManualSync).length === 1 ? '' : 's'}.
                </p>
              </InsetPanel>
            </div>

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

            <div className="space-y-4">
              {sources.length ? (
                sources.map((source) => {
                  const isConfigured = Boolean(source.credentials.clientId || source.credentials.hasSecret)
                  const statusTone = getSourceStatusTone(source)
                  const statusLabel = source.runtime.lastStatus ?? (isConfigured ? 'Configured' : 'Needs credentials')
                  const activity = describeSourceActivity(source)

                  return (
                    <InsetPanel key={source.key} className="space-y-4 p-4">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div className="space-y-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <p className="text-base font-semibold">{source.displayName}</p>
                            <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                              {source.key}
                            </Badge>
                            {!source.enabled ? <Badge variant="outline">Disabled</Badge> : null}
                          </div>
                          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                            <span>Last run {formatTimestamp(source.runtime.lastCompletedAt)}</span>
                            <span>Last success {formatTimestamp(source.runtime.lastSucceededAt)}</span>
                          </div>
                        </div>
                        <StatusBadge tone={statusTone}>{statusLabel}</StatusBadge>
                      </div>

                      <div className="grid gap-3 md:grid-cols-3">
                        <Metric label="Schedule" value={source.supportsScheduling ? source.syncSchedule : 'Worker-managed'} />
                        <Metric label="Client ID" value={source.credentials.clientId ? 'Configured' : 'Missing'} />
                        <Metric label="Secret" value={source.credentials.hasSecret ? 'Stored' : 'Missing'} />
                      </div>

                      <InsetPanel className="grid gap-3 px-4 py-3 md:grid-cols-[minmax(0,1fr)_repeat(3,minmax(0,160px))]">
                        <div className="space-y-1">
                          <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                            Ingestion activity
                          </p>
                          <p className="text-sm font-medium text-foreground">{activity.title}</p>
                          <p className="text-xs text-muted-foreground">{activity.description}</p>
                        </div>
                        <Metric label="Phase" value={activity.phase} compact />
                        <Metric label="Batch" value={activity.batch} compact />
                        <Metric label="Checkpoint" value={activity.checkpoint} compact />
                      </InsetPanel>

                      {source.runtime.lastError ? (
                        <InsetPanel emphasis="subtle" className="px-4 py-3 text-sm text-destructive">
                          Last error: {source.runtime.lastError}
                        </InsetPanel>
                      ) : null}

                      <div className="flex flex-wrap gap-2">
                        {source.supportsManualSync ? (
                          <Button
                            type="button"
                            variant="outline"
                            disabled={syncMutation.isPending || !source.enabled}
                            onClick={() => syncMutation.mutate(source.key)}
                          >
                            <RotateCw className="size-4" />
                            {syncingSourceKey === source.key ? 'Syncing...' : 'Manual sync'}
                          </Button>
                        ) : null}
                        <Button
                          type="button"
                          variant="outline"
                          onClick={() => onEditSource(source.key)}
                        >
                          <PenSquare className="size-4" />
                          Edit
                        </Button>
                        <Button
                          type="button"
                          variant="outline"
                          onClick={() => setHistorySourceKey(source.key)}
                        >
                          View full history
                        </Button>
                      </div>
                    </InsetPanel>
                  )
                })
              ) : (
                <InsetPanel emphasis="subtle" className="border-dashed px-4 py-8 text-sm text-muted-foreground">
                  No sources are available for the selected tenant.
                </InsetPanel>
              )}
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
          </>
        ) : null}
      </section>
    </TooltipProvider>
  )
}

function TenantSourceEditorPage({
  tenant,
  source,
  isSaving,
  saveState,
  onSave,
  onBack,
  onUpdateSource,
}: {
  tenant: TenantDetail
  source: TenantIngestionSourceDraft
  isSaving: boolean
  saveState: 'idle' | 'saved' | 'error'
  onSave: () => void
  onBack: () => void
  onUpdateSource: (
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) => void
}) {
  return (
    <div className="space-y-5">
      <div className="space-y-3">
        <Link
          to="/admin/sources"
          search={{ activeView: 'tenant' }}
          className="inline-flex items-center gap-2 text-sm text-muted-foreground transition-colors hover:text-foreground"
        >
          <ArrowLeft className="size-4" />
          Back to tenant sources
        </Link>
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-1">
            <h2 className="text-2xl font-semibold tracking-tight">{`Edit ${source.displayName}`}</h2>
            <p className="max-w-3xl text-sm leading-6 text-muted-foreground">
              Update runtime control, scheduling, and credentials for this ingestion source.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button type="button" variant="outline" onClick={onBack}>
              Cancel
            </Button>
            <Button onClick={onSave} disabled={isSaving}>
              {isSaving ? 'Saving...' : `Save ${source.displayName}`}
            </Button>
          </div>
        </div>
      </div>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_320px]">
        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardContent className="space-y-6 p-5">
              <FormSection title="Runtime control">
                <InsetPanel className="flex items-center justify-between gap-3 px-4 py-3">
                  <div className="space-y-1">
                    <p className="text-sm font-medium">Enable source</p>
                    <p className="text-xs text-muted-foreground">
                      Included in tenant ingestion schedule and credential validation.
                    </p>
                  </div>
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
                </InsetPanel>
              </FormSection>

              <FormSection title="Connection settings">
                <div className="grid gap-4 md:grid-cols-2">
                  <FieldBlock
                    label="Display Name"
                    tooltip="The operator-facing name shown for this source throughout the admin UI."
                    control={(
                      <Input
                        value={source.displayName}
                        onChange={(event) => {
                          onUpdateSource(source.key, (current) => ({
                            ...current,
                            displayName: event.target.value,
                          }))
                        }}
                        className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                      />
                    )}
                  />
                  {source.supportsScheduling ? (
                    <FieldBlock
                      label="Sync Schedule"
                      tooltip="Cron expression used by the worker for recurring ingestion."
                      control={(
                        <Input
                          value={source.syncSchedule}
                          onChange={(event) => {
                            onUpdateSource(source.key, (current) => ({
                              ...current,
                              syncSchedule: event.target.value,
                            }))
                          }}
                          className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                        />
                      )}
                    />
                  ) : (
                    <InsetPanel emphasis="subtle" className="px-4 py-3 text-sm text-muted-foreground">
                      This source does not currently support a dedicated schedule.
                    </InsetPanel>
                  )}
                </div>
              </FormSection>

              <FormSection title="Credentials">
                <div className="grid gap-4 md:grid-cols-2">
                  <InsetPanel emphasis="subtle" className="px-4 py-3 text-sm text-muted-foreground md:col-span-2">
                    This source automatically uses the selected tenant&apos;s Entra tenant ID:{' '}
                    <span className="font-medium text-foreground">{tenant.entraTenantId}</span>.
                  </InsetPanel>
                  <FieldBlock
                    label="Client ID"
                    tooltip="Application client identifier used to authenticate against the source."
                    control={(
                      <Input
                        value={source.credentials.clientId}
                        onChange={(event) => {
                          onUpdateSource(source.key, (current) => ({
                            ...current,
                            credentials: { ...current.credentials, clientId: event.target.value },
                          }))
                        }}
                        className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                      />
                    )}
                  />
                  <FieldBlock
                    label="Client Secret"
                    tooltip="Stored securely after save. Enter a new value only when rotating credentials."
                    className="md:col-span-2"
                    control={(
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
                        className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                      />
                    )}
                  />
                  <FieldBlock
                    label="API Base URL"
                    tooltip="Base endpoint used for provider API requests."
                    control={(
                      <Input
                        value={source.credentials.apiBaseUrl}
                        onChange={(event) => {
                          onUpdateSource(source.key, (current) => ({
                            ...current,
                            credentials: { ...current.credentials, apiBaseUrl: event.target.value },
                          }))
                        }}
                        className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                      />
                    )}
                  />
                  <FieldBlock
                    label="Token Scope"
                    tooltip="OAuth scope requested when acquiring access tokens for this source."
                    control={(
                      <Input
                        value={source.credentials.tokenScope}
                        onChange={(event) => {
                          onUpdateSource(source.key, (current) => ({
                            ...current,
                            credentials: { ...current.credentials, tokenScope: event.target.value },
                          }))
                        }}
                        className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                      />
                    )}
                  />
                </div>
              </FormSection>
          </CardContent>
        </Card>

        <div className="space-y-4">
          {saveState === 'saved' ? <StatusPanel tone="success" message="Source configuration saved." /> : null}
          {saveState === 'error' ? <StatusPanel tone="error" message="Saving failed. Review the values and try again." /> : null}
          <Card className="rounded-2xl border-border/70 bg-card/75">
            <CardHeader>
              <h3 className="text-base font-medium">Source posture</h3>
            </CardHeader>
            <CardContent className="space-y-3 text-sm text-muted-foreground">
              <InsetPanel emphasis="subtle" className="px-4 py-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Last run</p>
                <p className="mt-1 font-medium text-foreground">{formatTimestamp(source.runtime.lastCompletedAt)}</p>
              </InsetPanel>
              <InsetPanel emphasis="subtle" className="px-4 py-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Last success</p>
                <p className="mt-1 font-medium text-foreground">{formatTimestamp(source.runtime.lastSucceededAt)}</p>
              </InsetPanel>
              <InsetPanel emphasis="subtle" className="px-4 py-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Status</p>
                <p className="mt-1 font-medium text-foreground">{source.runtime.lastStatus ?? 'Unknown'}</p>
              </InsetPanel>
              {source.runtime.lastError ? (
                <InsetPanel className="px-4 py-3 text-sm text-destructive">
                  Last error: {source.runtime.lastError}
                </InsetPanel>
              ) : null}
            </CardContent>
          </Card>
          <Card className="rounded-2xl border-border/70 bg-card/75">
            <CardHeader>
              <h3 className="text-base font-medium">Navigation</h3>
            </CardHeader>
            <CardContent className="text-sm text-muted-foreground">
              Save keeps you on this page so you can review status or continue refining the source settings.
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  )
}

function StatusPanel({
  tone,
  message,
}: {
  tone: 'success' | 'error'
  message: string
}) {
  return (
    <InsetPanel
      className={cn(
        'px-4 py-3 text-sm',
        tone === 'success' ? 'text-emerald-700 dark:text-emerald-300' : 'text-destructive',
      )}
    >
      {message}
    </InsetPanel>
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

function Metric({
  label,
  value,
  compact = false,
}: {
  label: string
  value: string
  compact?: boolean
}) {
  return (
    <InsetPanel emphasis="strong" className={cn('px-3 py-2', compact && 'min-h-0')}>
      <p className="text-[11px] uppercase tracking-[0.12em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </InsetPanel>
  )
}

function describeSourceActivity(source: TenantIngestionSourceDraft) {
  if (source.runtime.activeIngestionRunId && source.runtime.activePhase) {
    const runtimeStatus = source.runtime.lastStatus?.toLowerCase()
    const title =
      runtimeStatus === 'merging'
        ? 'Merge in progress'
        : runtimeStatus === 'mergepending'
          ? 'Waiting to merge'
          : 'Ingestion running'

    return {
      title,
      description: `Lease active until ${formatTimestamp(source.runtime.leaseExpiresAt)}. Latest checkpoint ${formatTimestamp(source.runtime.activeCheckpointCommittedAt)}.`,
      phase: formatPhase(source.runtime.activePhase),
      batch:
        source.runtime.activeBatchNumber !== null
          ? String(source.runtime.activeBatchNumber)
          : '—',
      checkpoint: source.runtime.activeCheckpointStatus ?? 'Running',
    }
  }

  const latestRun = source.recentRuns[0]
  const latestRunStatus = latestRun?.status.toLowerCase()
  if (
    latestRunStatus === 'failedrecoverable'
    || latestRunStatus === 'failedterminal'
    || latestRunStatus === 'failed'
  ) {
    return {
      title:
        latestRunStatus === 'failedrecoverable'
          ? 'Recoverable failure'
          : latestRunStatus === 'failedterminal'
            ? 'Terminal failure'
            : 'Last run failed',
      description:
        latestRunStatus === 'failedrecoverable'
          ? 'Staged snapshots are retained for up to 24 hours and can resume from the last committed checkpoint.'
          : latestRunStatus === 'failedterminal'
            ? 'This run requires operator action before retrying, but failed staged data will still be discarded after 24 hours.'
            : 'Failed staged snapshots are retained for up to 24 hours before they are discarded.',
      phase: formatPhase(latestRun.latestPhase),
      batch:
        latestRun.latestBatchNumber !== null ? String(latestRun.latestBatchNumber) : '—',
      checkpoint: latestRun.latestCheckpointStatus ?? 'Failed',
    }
  }

  return {
    title: 'Idle',
    description: `Last completed ${formatTimestamp(source.runtime.lastCompletedAt)}.`,
    phase: 'Ready',
    batch: '—',
    checkpoint: source.runtime.lastStatus || 'Idle',
  }
}

function FormSection({
  title,
  children,
}: {
  title: string
  children: React.ReactNode
}) {
  return (
    <div className="space-y-5">
      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{title}</p>
      {children}
      <Separator className="opacity-60" />
    </div>
  )
}

function FieldBlock({
  label,
  tooltip,
  control,
  className,
}: {
  label: string
  tooltip?: string
  control: React.ReactNode
  className?: string
}) {
  return (
    <div className={cn('grid content-start gap-2', className)}>
      <div className="flex min-h-5 items-center gap-2">
        <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</span>
        {tooltip ? (
          <Tooltip>
            <TooltipTrigger className="inline-flex items-center text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
              <CircleHelp className="size-3.5" />
            </TooltipTrigger>
            <TooltipContent
              align="start"
              className="max-w-sm rounded-lg border border-border/80 bg-popover px-3 py-2 text-xs leading-5 text-popover-foreground shadow-lg"
            >
              {tooltip}
            </TooltipContent>
          </Tooltip>
        ) : null}
      </div>
      {control}
    </div>
  )
}

function getSourceStatusTone(source: TenantIngestionSourceDraft): 'neutral' | 'success' | 'warning' | 'error' {
  if (source.runtime.lastError) {
    return 'error'
  }

  const normalizedStatus = source.runtime.lastStatus?.toLowerCase()
  if (normalizedStatus === 'staging' || normalizedStatus === 'mergepending' || normalizedStatus === 'merging') {
    return 'warning'
  }

  if (source.runtime.lastSucceededAt) {
    return 'success'
  }

  return 'neutral'
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

function formatPhase(value: string | null | undefined) {
  if (!value) {
    return '—'
  }

  return value
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}
