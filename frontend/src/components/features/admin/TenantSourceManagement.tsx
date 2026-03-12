import { useMemo, useState } from 'react'
import { useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { CircleHelp, PenSquare, RotateCw } from 'lucide-react'
import { triggerTenantIngestionSync, updateTenant } from '@/api/settings.functions'
import type { TenantDetail, TenantIngestionSource } from '@/api/settings.schemas'
import { SourceRunHistorySheet } from '@/components/features/admin/SourceRunHistorySheet'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Separator } from '@/components/ui/separator'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

type TenantSourceManagementProps = {
  tenant: TenantDetail
}

type TenantIngestionSourceDraft = Omit<TenantIngestionSource, 'credentials'> & {
  credentials: TenantIngestionSource['credentials'] & {
    secret: string
  }
}

export function TenantSourceManagement({ tenant }: TenantSourceManagementProps) {
  const router = useRouter()
  const [sources, setSources] = useState(() => tenant.ingestionSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [syncingSourceKey, setSyncingSourceKey] = useState<string | null>(null)
  const [syncState, setSyncState] = useState<'idle' | 'success' | 'error'>('idle')
  const [historySourceKey, setHistorySourceKey] = useState<string | null>(null)
  const [editingSourceKey, setEditingSourceKey] = useState<string | null>(null)
  const [sheetOpen, setSheetOpen] = useState(false)

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
      setSheetOpen(false)
      setEditingSourceKey(null)
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
                          onClick={() => {
                            setEditingSourceKey(source.key)
                            setSheetOpen(true)
                          }}
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

        <TenantSourceSheet
          tenant={tenant}
          source={editingSource}
          isSaving={mutation.isPending}
          onOpenChange={(open) => {
            setSheetOpen(open)
            if (!open) {
              setEditingSourceKey(null)
            }
          }}
          open={sheetOpen}
          onSave={() => mutation.mutate()}
          onUpdateSource={updateSource}
        />

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
    </TooltipProvider>
  )
}

function TenantSourceSheet({
  tenant,
  source,
  open,
  isSaving,
  onOpenChange,
  onSave,
  onUpdateSource,
}: {
  tenant: TenantDetail
  source: TenantIngestionSourceDraft | null
  open: boolean
  isSaving: boolean
  onOpenChange: (open: boolean) => void
  onSave: () => void
  onUpdateSource: (
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) => void
}) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full sm:max-w-2xl">
        <SheetHeader className="border-b border-border/60">
          <SheetTitle>{source ? `Edit ${source.displayName}` : 'Edit source'}</SheetTitle>
          <SheetDescription>
            Update runtime control, scheduling, and credentials for this ingestion source.
          </SheetDescription>
        </SheetHeader>

        {source ? (
          <>
            <div className="flex-1 space-y-6 overflow-y-auto p-5">
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
            </div>

            <SheetFooter className="border-t border-border/60">
              <Button onClick={onSave} disabled={isSaving} className="rounded-full px-5">
                {isSaving ? 'Saving...' : `Save ${source.displayName}`}
              </Button>
            </SheetFooter>
          </>
        ) : null}
      </SheetContent>
    </Sheet>
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

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <InsetPanel emphasis="strong" className="px-3 py-2">
      <p className="text-[11px] uppercase tracking-[0.12em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </InsetPanel>
  )
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

  if (source.runtime.lastStatus?.toLowerCase() === 'running') {
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
