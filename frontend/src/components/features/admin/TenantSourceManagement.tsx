import { useState } from 'react'
import { useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { KeyRound, RotateCw } from 'lucide-react'
import { triggerTenantIngestionSync, updateTenant } from '@/api/settings.functions'
import type { TenantDetail, TenantIngestionSource } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

type TenantSourceManagementProps = {
  tenant: TenantDetail
}

export function TenantSourceManagement({ tenant }: TenantSourceManagementProps) {
  const router = useRouter()
  const [sources, setSources] = useState(() => tenant.ingestionSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [syncingSourceKey, setSyncingSourceKey] = useState<string | null>(null)
  const [syncState, setSyncState] = useState<'idle' | 'success' | 'error'>('idle')

  const mutation = useMutation({
    mutationFn: async () => {
      await updateTenant({
        data: {
          tenantId: tenant.id,
          name: tenant.name,
          ingestionSources: sources.map((source) => ({
            key: source.key,
            displayName: source.displayName,
            enabled: source.enabled,
            syncSchedule: source.syncSchedule,
            credentials: {
              tenantId: source.credentials.tenantId,
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

  const ingestionSources = sources.filter((source) => source.key !== 'nvd')
  const enrichmentSources = sources.filter((source) => source.key === 'nvd')

  return (
    <section className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-2xl font-semibold tracking-[-0.03em]">Sources</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Configure tenant ingestion sources separately from enrichment providers.
          </p>
        </div>
        <Button onClick={() => mutation.mutate()} disabled={mutation.isPending}>
          {mutation.isPending ? 'Saving...' : 'Save source changes'}
        </Button>
      </div>

      <SourceSection
        title="Tenant Sources"
        description="Primary ingestion sources that discover tenant assets and vulnerabilities."
        sources={ingestionSources}
        syncingSourceKey={syncingSourceKey}
        syncMutation={syncMutation}
        onUpdateSource={updateSource}
      />

      <SourceSection
        title="Enrichment Sources"
        description="Secondary providers that enrich ingested vulnerabilities with additional context such as CVSS vectors and published dates."
        sources={enrichmentSources}
        syncingSourceKey={syncingSourceKey}
        syncMutation={syncMutation}
        onUpdateSource={updateSource}
      />

      {saveState === 'saved' ? <p className="text-sm text-emerald-300">Source configuration saved.</p> : null}
      {saveState === 'error' ? <p className="text-sm text-destructive">Save failed. Try again.</p> : null}
      {syncState === 'success' ? <p className="text-sm text-emerald-300">Ingestion sync started.</p> : null}
      {syncState === 'error' ? <p className="text-sm text-destructive">Sync trigger failed. Try again.</p> : null}
    </section>
  )
}

function SourceSection({
  title,
  description,
  sources,
  syncingSourceKey,
  syncMutation,
  onUpdateSource,
}: {
  title: string
  description: string
  sources: TenantIngestionSourceDraft[]
  syncingSourceKey: string | null
  syncMutation: ReturnType<typeof useMutation<void, Error, string>>
  onUpdateSource: (
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) => void
}) {
  return (
    <section className="space-y-4">
      <div className="space-y-1">
        <h3 className="text-lg font-semibold">{title}</h3>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
      {sources.map((source) => {
        const isConfigured = Boolean(
          source.key === 'nvd'
            ? source.credentials.hasSecret
            : source.credentials.tenantId || source.credentials.clientId || source.credentials.hasSecret,
        )
        const isEnrichmentSource = source.key === 'nvd'
        const secretLabel = isEnrichmentSource ? 'API Key' : 'Client Secret'
        const sourceSummary = isEnrichmentSource
          ? 'Configure NVD enrichment. When enabled, worker-driven syncs will look up each CVE in NVD and fill missing description, CVSS vector, and published date.'
          : 'Configure API credentials and the schedule string used for sync orchestration.'

        return (
          <Card key={source.key} className="rounded-[28px] border-border/70 bg-card/82">
            <CardHeader>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="flex items-center gap-2">
                    <CardTitle>{source.displayName}</CardTitle>
                    <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                      {source.key}
                    </Badge>
                    <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 text-primary">
                      {isEnrichmentSource ? 'Enrichment' : 'Ingestion'}
                    </Badge>
                  </div>
                  <p className="mt-1 text-sm text-muted-foreground">{sourceSummary}</p>
                </div>
                <Badge
                  variant="outline"
                  className={isConfigured
                    ? 'rounded-full border-emerald-400/25 bg-emerald-400/10 text-emerald-200'
                    : 'rounded-full border-border/70 bg-background/60 text-muted-foreground'}
                >
                  {isConfigured ? 'Configured' : 'Needs credentials'}
                </Badge>
              </div>
              <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                <span>{isEnrichmentSource ? 'Last enrichment' : 'Last ingestion'}: {formatTimestamp(source.runtime.lastCompletedAt)}</span>
                {source.runtime.lastStatus ? (
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/60 text-muted-foreground">
                    {source.runtime.lastStatus}
                  </Badge>
                ) : null}
              </div>
            </CardHeader>
            <CardContent className="space-y-5">
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
                    {isEnrichmentSource ? 'Used during worker enrichment of vulnerability data.' : 'Included in tenant ingestion schedule and credential validation.'}
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
                    Worker-driven enrichment with no dedicated schedule.
                  </div>
                )}
              </div>

              <div className="grid gap-4 md:grid-cols-2">
                {!isEnrichmentSource ? (
                  <>
                    <label className="space-y-2">
                      <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Credential Tenant ID</span>
                      <Input
                        value={source.credentials.tenantId}
                        onChange={(event) => {
                          onUpdateSource(source.key, (current) => ({
                            ...current,
                            credentials: { ...current.credentials, tenantId: event.target.value },
                          }))
                        }}
                      />
                    </label>
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
                  </>
                ) : null}
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
                {!isEnrichmentSource ? (
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
                ) : null}
              </div>
            </CardContent>
          </Card>
        )
      })}
    </section>
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
