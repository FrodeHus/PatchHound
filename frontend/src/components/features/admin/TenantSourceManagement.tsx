import { useMemo, useState } from 'react'
import { Link, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { toast } from 'sonner'
import { ArrowLeft, CircleHelp, Clock, PenSquare, RotateCw, Square, X } from 'lucide-react'
import { abortTenantIngestionRun, triggerTenantIngestionSync, updateTenant } from '@/api/settings.functions'
import type { TenantDetail, TenantIngestionSource } from '@/api/settings.schemas'
import { SourceRunHistoryView } from '@/components/features/admin/SourceRunHistorySheet'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
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
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import { getApiErrorMessage } from '@/lib/api-errors'
import { cn } from '@/lib/utils'

type TenantSourceManagementProps = {
  tenant: TenantDetail
  editingSourceKey: string | null
  historySourceKey: string | null
  onEditSource: (sourceKey: string) => void
  onOpenHistory: (sourceKey: string) => void
  onCloseEditor: () => void
  onCloseHistory: () => void
}

type TenantIngestionSourceDraft = Omit<TenantIngestionSource, 'credentials'> & {
  credentials: TenantIngestionSource['credentials'] & {
    secret: string
  }
}

export function TenantSourceManagement({
  tenant,
  editingSourceKey,
  historySourceKey,
  onEditSource,
  onOpenHistory,
  onCloseEditor,
  onCloseHistory,
}: TenantSourceManagementProps) {
  const router = useRouter()
  const [sources, setSources] = useState(() => tenant.ingestionSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [syncingSourceKey, setSyncingSourceKey] = useState<string | null>(null)
  const [abortingRunId, setAbortingRunId] = useState<string | null>(null)

  const editingSource = useMemo(
    () => sources.find((source) => source.key === editingSourceKey) ?? null,
    [editingSourceKey, sources],
  )
  const historySource = useMemo(
    () => sources.find((source) => source.key === historySourceKey) ?? null,
    [historySourceKey, sources],
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
      toast.success('Source configuration saved')
      await router.invalidate()
    },
    onError: (error) => {
      setSaveState('error')
      toast.error(getApiErrorMessage(error, 'Failed to save source configuration'))
    },
  })

  const syncMutation = useMutation({
    mutationFn: async (sourceKey: string) => {
      await triggerTenantIngestionSync({ data: { tenantId: tenant.id, sourceKey } })
    },
    onMutate: (sourceKey) => {
      setSyncingSourceKey(sourceKey)
    },
    onSuccess: async () => {
      toast.success('Sync queued')
      await router.invalidate()
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to start sync'))
    },
    onSettled: () => {
      setSyncingSourceKey(null)
    },
  })

  const abortMutation = useMutation({
    mutationFn: async ({ sourceKey, runId }: { sourceKey: string; runId: string }) => {
      await abortTenantIngestionRun({ data: { tenantId: tenant.id, sourceKey, runId } })
    },
    onMutate: ({ runId }) => {
      setAbortingRunId(runId)
    },
    onSuccess: async () => {
      toast.success('Abort requested')
      await router.invalidate()
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to abort ingestion'))
    },
    onSettled: () => {
      setAbortingRunId(null)
    },
  })

  function updateSource(
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) {
    setSaveState('idle')
    setSources((current) => current.map((source) => (source.key === key ? mutate(source) : source)))
  }

  // Full-page history view takes over when open
  if (historySource) {
    return (
      <TenantSourceHistoryPage
        tenant={tenant}
        source={historySource}
        onBack={onCloseHistory}
      />
    )
  }

  return (
    <TooltipProvider>
      <section className="space-y-4">
        <div className="space-y-1">
          <h2 className="text-lg font-semibold">Tenant Sources</h2>
          <p className="text-sm text-muted-foreground">
            Credentials and schedules used by the worker to collect inventory
            and vulnerability data.
          </p>
        </div>

        {sources.length ? (
          <div className="overflow-hidden rounded-xl border border-border/70">
            <Table>
              <TableHeader>
                <TableRow className="border-border/60 hover:bg-transparent">
                  <TableHead className="h-9 pl-4 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                    Source
                  </TableHead>
                  <TableHead className="h-9 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                    Status
                  </TableHead>
                  <TableHead className="h-9 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                    Last run
                  </TableHead>
                  <TableHead className="h-9 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                    Credentials
                  </TableHead>
                  <TableHead className="h-9 pr-4 text-right text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                    Actions
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {sources.map((source) => {
                  const isConfigured = Boolean(
                    source.credentials.clientId || source.credentials.hasSecret,
                  );
                  const statusTone = getSourceStatusTone(source);
                  const statusLabel =
                    source.runtime.lastStatus ??
                    (isConfigured ? "Configured" : "Needs credentials");
                  const recoverableFailure = isRecoverableFailure(source);
                  const activeRunId = source.runtime.activeIngestionRunId;

                  return (
                    <TableRow
                      key={source.key}
                      className={cn(
                        "border-border/50",
                        editingSourceKey === source.key && "bg-primary/[0.04]",
                      )}
                    >
                      {/* Source name + key */}
                      <TableCell className="py-3 pl-4">
                        <div className="flex items-center gap-2">
                          <span className="font-medium text-foreground">
                            {source.displayName}
                          </span>
                          <span className="rounded-full border border-border/60 bg-muted/30 px-2 py-0.5 font-mono text-[11px] text-muted-foreground">
                            {source.key}
                          </span>
                          {!source.enabled ? (
                            <Badge variant="outline" className="text-[11px]">
                              Disabled
                            </Badge>
                          ) : null}
                        </div>
                        <p className="mt-0.5 text-[11px] text-muted-foreground">
                          {source.supportsScheduling
                            ? source.syncSchedule
                            : "Worker-managed"}
                        </p>
                      </TableCell>

                      {/* Status */}
                      <TableCell className="py-3">
                        <StatusBadge tone={statusTone}>
                          {statusLabel}
                        </StatusBadge>
                      </TableCell>

                      {/* Last run */}
                      <TableCell className="py-3">
                        <p className="text-[13px]">
                          {formatTimestamp(source.runtime.lastCompletedAt)}
                        </p>
                        {source.runtime.lastSucceededAt !==
                        source.runtime.lastCompletedAt ? (
                          <p className="mt-0.5 text-[11px] text-muted-foreground">
                            Last success{" "}
                            {formatTimestamp(source.runtime.lastSucceededAt)}
                          </p>
                        ) : null}
                        {source.runtime.lastError ? (
                          <p
                            className="mt-0.5 max-w-[160px] truncate text-[11px] text-destructive"
                            title={source.runtime.lastError}
                          >
                            {source.runtime.lastError}
                          </p>
                        ) : null}
                      </TableCell>

                      {/* Credentials */}
                      <TableCell className="py-3">
                        <div className="space-y-1">
                          <CredentialDot
                            label="Client ID"
                            ok={Boolean(source.credentials.clientId)}
                          />
                          <CredentialDot
                            label="Secret"
                            ok={source.credentials.hasSecret}
                          />
                        </div>
                      </TableCell>

                      {/* Actions */}
                      <TableCell className="py-3 pr-4">
                        <div className="flex items-center justify-end gap-1.5">
                          {activeRunId ? (
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              disabled={abortMutation.isPending}
                              onClick={() =>
                                abortMutation.mutate({
                                  sourceKey: source.key,
                                  runId: activeRunId,
                                })
                              }
                            >
                              <Square className="size-3" />
                              {abortingRunId === activeRunId
                                ? "Aborting…"
                                : "Abort"}
                            </Button>
                          ) : null}
                          {source.supportsManualSync && !activeRunId ? (
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              disabled={
                                syncMutation.isPending || !source.enabled
                              }
                              onClick={() => syncMutation.mutate(source.key)}
                            >
                              <RotateCw className="size-3" />
                              {syncingSourceKey === source.key
                                ? "Syncing…"
                                : recoverableFailure
                                  ? "Resume"
                                  : "Sync"}
                            </Button>
                          ) : null}
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            onClick={() => onEditSource(source.key)}
                          >
                            <PenSquare className="size-3" />
                            Edit
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon-sm"
                            title="View run history"
                            onClick={() => onOpenHistory(source.key)}
                          >
                            <Clock className="size-3.5" />
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        ) : (
          <InsetPanel
            emphasis="subtle"
            className="border-dashed px-4 py-8 text-sm text-muted-foreground"
          >
            No sources are available for the selected tenant.
          </InsetPanel>
        )}

        {/* Editor sheet — open when editingSourceKey is set */}
        <Sheet
          open={editingSource !== null}
          onOpenChange={(open) => {
            if (!open) onCloseEditor();
          }}
        >
          <SheetContent
            showCloseButton={false}
            className="flex flex-col gap-0 overflow-hidden p-0 max-w-xl"
            side="right"
          >
            {editingSource ? (
              <TenantSourceEditorSheetContent
                tenant={tenant}
                source={editingSource}
                isSaving={mutation.isPending}
                saveState={saveState}
                onSave={() => mutation.mutate()}
                onClose={onCloseEditor}
                onUpdateSource={updateSource}
                onViewHistory={() => {
                  onCloseEditor();
                  onOpenHistory(editingSource.key);
                }}
              />
            ) : null}
          </SheetContent>
        </Sheet>
      </section>
    </TooltipProvider>
  );
}

// ─── Editor sheet content ───────────────────────────────────────────────────

function TenantSourceEditorSheetContent({
  tenant,
  source,
  isSaving,
  saveState,
  onSave,
  onClose,
  onUpdateSource,
  onViewHistory,
}: {
  tenant: TenantDetail
  source: TenantIngestionSourceDraft
  isSaving: boolean
  saveState: 'idle' | 'saved' | 'error'
  onSave: () => void
  onClose: () => void
  onUpdateSource: (
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) => void
  onViewHistory: () => void
}) {
  return (
    <>
      <SheetHeader className="shrink-0 border-b border-border/60 p-5">
        <div className="flex items-start justify-between gap-3 pr-1">
          <div>
            <SheetTitle>Edit {source.displayName}</SheetTitle>
            <SheetDescription className="mt-1">
              Update runtime control, scheduling, and credentials for this
              source.
            </SheetDescription>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            onClick={onClose}
            className="-mr-1 -mt-1 shrink-0"
          >
            <X className="size-4" />
          </Button>
        </div>
      </SheetHeader>

      <div className="flex-1 overflow-y-auto">
        {/* Posture summary */}
        <div className="space-y-3 border-b border-border/60 p-5">
          <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
            Source posture
          </p>
          <div className="divide-y divide-border/50 overflow-hidden rounded-lg border border-border/60">
            <PostureRow
              label="Status"
              value={source.runtime.lastStatus ?? "Unknown"}
            />
            <PostureRow
              label="Last run"
              value={formatTimestamp(source.runtime.lastCompletedAt)}
            />
            <PostureRow
              label="Last success"
              value={formatTimestamp(source.runtime.lastSucceededAt)}
            />
          </div>
          {source.runtime.lastError ? (
            <p className="text-xs text-destructive">
              Error: {source.runtime.lastError}
            </p>
          ) : null}
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="w-full"
            onClick={onViewHistory}
          >
            <Clock className="size-3.5" />
            View full run history
          </Button>
        </div>

        {/* Form */}
        <div className="space-y-6 p-5">
          <FormSection title="Runtime control">
            <label className="flex cursor-pointer items-center justify-between gap-3 rounded-lg border border-border/60 px-4 py-3 transition-colors hover:bg-muted/20">
              <div className="space-y-0.5">
                <p className="text-sm font-medium">Enable source</p>
                <p className="text-xs text-muted-foreground">
                  Included in tenant ingestion schedule and credential
                  validation.
                </p>
              </div>
              <input
                type="checkbox"
                checked={source.enabled}
                onChange={(event) => {
                  onUpdateSource(source.key, (current) => ({
                    ...current,
                    enabled: event.target.checked,
                  }));
                }}
              />
            </label>
          </FormSection>

          <FormSection title="Connection settings">
            <div className="grid gap-4 sm:grid-cols-2">
              <FieldBlock
                label="Display Name"
                tooltip="The operator-facing name shown for this source throughout the admin UI."
                control={
                  <Input
                    value={source.displayName}
                    onChange={(event) => {
                      onUpdateSource(source.key, (current) => ({
                        ...current,
                        displayName: event.target.value,
                      }));
                    }}
                    className="h-10"
                  />
                }
              />
              {source.supportsScheduling ? (
                <FieldBlock
                  label="Sync Schedule"
                  tooltip="Cron expression used by the worker for recurring ingestion."
                  control={
                    <Input
                      value={source.syncSchedule}
                      onChange={(event) => {
                        onUpdateSource(source.key, (current) => ({
                          ...current,
                          syncSchedule: event.target.value,
                        }));
                      }}
                      className="h-10"
                    />
                  }
                />
              ) : (
                <div className="flex items-center rounded-lg border border-border/50 bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
                  Schedule is worker-managed for this source.
                </div>
              )}
            </div>
          </FormSection>

          <FormSection title="Credentials">
            <div className="space-y-4">
              <div className="rounded-lg border border-border/50 bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
                Uses tenant Entra ID:{" "}
                <span className="font-medium text-foreground">
                  {tenant.entraTenantId}
                </span>
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <FieldBlock
                  label="Client ID"
                  tooltip="Application client identifier used to authenticate against the source."
                  className="col-span-2"
                  control={
                    <Input
                      value={source.credentials.clientId}
                      onChange={(event) => {
                        onUpdateSource(source.key, (current) => ({
                          ...current,
                          credentials: {
                            ...current.credentials,
                            clientId: event.target.value,
                          },
                        }));
                      }}
                      className="h-10"
                    />
                  }
                />
                <FieldBlock
                  label="Client Secret"
                  tooltip="Stored securely after save. Enter a new value only when rotating credentials."
                  className="sm:col-span-2"
                  control={
                    <Input
                      type="password"
                      value={source.credentials.secret}
                      placeholder={
                        source.credentials.hasSecret
                          ? "Stored in OpenBao — enter a new value to rotate"
                          : "Not configured"
                      }
                      onChange={(event) => {
                        onUpdateSource(source.key, (current) => ({
                          ...current,
                          credentials: {
                            ...current.credentials,
                            secret: event.target.value,
                            hasSecret:
                              current.credentials.hasSecret ||
                              event.target.value.trim().length > 0,
                          },
                        }));
                      }}
                      className="h-10"
                    />
                  }
                />
                <FieldBlock
                  label="API Base URL"
                  tooltip="Base endpoint used for provider API requests."
                  className="col-span-2"
                  control={
                    <Input
                      value={source.credentials.apiBaseUrl}
                      onChange={(event) => {
                        onUpdateSource(source.key, (current) => ({
                          ...current,
                          credentials: {
                            ...current.credentials,
                            apiBaseUrl: event.target.value,
                          },
                        }));
                      }}
                      className="h-10"
                    />
                  }
                />
                <FieldBlock
                  label="Token Scope"
                  tooltip="OAuth scope requested when acquiring access tokens for this source."
                  className="col-span-2"
                  control={
                    <Input
                      value={source.credentials.tokenScope}
                      onChange={(event) => {
                        onUpdateSource(source.key, (current) => ({
                          ...current,
                          credentials: {
                            ...current.credentials,
                            tokenScope: event.target.value,
                          },
                        }));
                      }}
                      className="h-10"
                    />
                  }
                />
              </div>
            </div>
          </FormSection>
        </div>
      </div>

      <SheetFooter className="mt-0 shrink-0 flex-row items-center justify-between gap-3 border-t border-border/60 p-5">
        <div className="text-xs">
          {saveState === "saved" ? (
            <span className="text-tone-success-foreground">
              Configuration saved
            </span>
          ) : null}
          {saveState === "error" ? (
            <span className="text-destructive">
              Save failed — review and retry
            </span>
          ) : null}
        </div>
        <div className="flex gap-2">
          <Button type="button" variant="outline" onClick={onClose}>
            Close
          </Button>
          <Button onClick={onSave} disabled={isSaving}>
            {isSaving ? "Saving…" : `Save ${source.displayName}`}
          </Button>
        </div>
      </SheetFooter>
    </>
  );
}

// ─── History full-page view ─────────────────────────────────────────────────

function TenantSourceHistoryPage({
  tenant,
  source,
  onBack,
}: {
  tenant: TenantDetail
  source: TenantIngestionSourceDraft
  onBack: () => void
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
            <h2 className="text-2xl font-semibold tracking-tight">{`${source.displayName} history`}</h2>
            <p className="max-w-3xl text-sm leading-6 text-muted-foreground">
              Review ingestion batches, merge progress, and failure posture for this source.
            </p>
          </div>
          <Button type="button" variant="outline" onClick={onBack}>
            Close history
          </Button>
        </div>
      </div>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_320px]">
        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardContent className="p-5">
            <SourceRunHistoryView
              tenantId={tenant.id}
              sourceKey={source.key}
              sourceDisplayName={source.displayName}
            />
          </CardContent>
        </Card>

        <div className="space-y-4">
          <Card className="rounded-2xl border-border/70 bg-card/75">
            <CardHeader>
              <h3 className="text-base font-medium">Source posture</h3>
            </CardHeader>
            <CardContent className="space-y-3 text-sm text-muted-foreground">
              <InsetPanel emphasis="subtle" className="px-4 py-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Current status</p>
                <p className="mt-1 font-medium text-foreground">{source.runtime.lastStatus ?? 'Unknown'}</p>
              </InsetPanel>
              <InsetPanel emphasis="subtle" className="px-4 py-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Last run</p>
                <p className="mt-1 font-medium text-foreground">{formatTimestamp(source.runtime.lastCompletedAt)}</p>
              </InsetPanel>
              <InsetPanel emphasis="subtle" className="px-4 py-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Last success</p>
                <p className="mt-1 font-medium text-foreground">{formatTimestamp(source.runtime.lastSucceededAt)}</p>
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
            <CardContent className="space-y-3 text-sm text-muted-foreground">
              <InsetPanel emphasis="subtle" className="px-4 py-3">
                Use the source list for operational overview, then open full history here when you need batch-level or failure-level detail.
              </InsetPanel>
              <Button type="button" variant="outline" className="w-full justify-start" onClick={onBack}>
                <ArrowLeft className="size-4" />
                Back to source list
              </Button>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  )
}

// ─── Shared small components ────────────────────────────────────────────────

function CredentialDot({ label, ok }: { label: string; ok: boolean }) {
  return (
    <div className="flex items-center gap-1.5">
      <span
        className={cn(
          'size-1.5 rounded-full',
          ok ? 'bg-tone-success-foreground' : 'bg-destructive/70',
        )}
      />
      <span className="text-[11px] text-muted-foreground">{label}</span>
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
        <span className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</span>
        {tooltip ? (
          <Tooltip>
            <TooltipTrigger className="inline-flex items-center text-muted-foreground/70 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
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

// ─── Logic helpers ──────────────────────────────────────────────────────────

function getSourceStatusTone(source: TenantIngestionSourceDraft): 'neutral' | 'success' | 'warning' | 'error' {
  if (source.runtime.lastError) return 'error'

  const normalizedStatus = source.runtime.lastStatus?.toLowerCase()
  if (normalizedStatus === 'staging' || normalizedStatus === 'mergepending' || normalizedStatus === 'merging') {
    return 'warning'
  }

  if (source.runtime.lastSucceededAt) return 'success'
  return 'neutral'
}

function isRecoverableFailure(source: TenantIngestionSourceDraft) {
  const latestRunStatus = source.recentRuns[0]?.status?.toLowerCase()
  return latestRunStatus === 'failedrecoverable'
}

function mapSourceToDraft(source: TenantIngestionSource): TenantIngestionSourceDraft {
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
