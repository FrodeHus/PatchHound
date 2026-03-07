import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Activity, KeyRound, Sparkles } from 'lucide-react'
import { type EnrichmentSource, updateEnrichmentSources } from '@/server/system.functions'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

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
      <Card className="rounded-[30px] border-border/70 bg-card/82 shadow-sm">
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
                {saveState === 'saved' ? <p className="rounded-full border border-emerald-400/25 bg-emerald-400/10 px-3 py-1 text-emerald-300">Configuration saved</p> : null}
                {saveState === 'error' ? <p className="rounded-full border border-destructive/25 bg-destructive/10 px-3 py-1 text-destructive">Save failed</p> : null}
              </div>
            </div>

            <div className="mt-5 space-y-4">
              {sources.map((source) => (
                <Card key={source.key} className="rounded-[26px] border-border/70 bg-card/82 shadow-sm">
                  <CardHeader className="border-b border-border/60 pb-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2">
                          <div className="flex size-9 items-center justify-center rounded-2xl border border-primary/20 bg-primary/10 text-primary">
                            <Sparkles className="size-4" />
                          </div>
                          <CardTitle>{source.displayName}</CardTitle>
                          <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                            {source.key}
                          </Badge>
                        </div>
                        <p className="mt-1 text-sm text-muted-foreground">
                          Shared enrichment used to fill missing CVSS and vulnerability context across tenants.
                        </p>
                      </div>
                      <Badge
                        variant="outline"
                        className={source.credentials.hasSecret
                          ? 'rounded-full border-emerald-400/25 bg-emerald-400/10 text-emerald-200'
                          : 'rounded-full border-border/70 bg-background/60 text-muted-foreground'}
                      >
                        {source.credentials.hasSecret ? 'Configured' : 'Needs credentials'}
                      </Badge>
                    </div>
                    <div className="mt-4 grid gap-3 md:grid-cols-3">
                      <StatusMetric
                        icon={Activity}
                        label="Worker Status"
                        value={source.runtime.lastStatus || 'Unknown'}
                        helper={getProviderStatusDescription(source)}
                      />
                      <StatusMetric
                        icon={Sparkles}
                        label="Last Success"
                        value={formatTimestamp(source.runtime.lastSucceededAt)}
                        helper={`Last completed run: ${formatTimestamp(source.runtime.lastCompletedAt)}`}
                      />
                      <StatusMetric
                        icon={KeyRound}
                        label="Credential State"
                        value={source.credentials.hasSecret ? 'Stored' : 'Missing'}
                        helper={source.credentials.hasSecret ? 'The provider can authenticate once enabled.' : 'Add an API key before enabling the provider.'}
                      />
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-5 pt-5">
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
                </Card>
              ))}
            </div>
          </div>
        </CardContent>
      </Card>
    </section>
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
    return 'rounded-full border border-amber-400/30 bg-amber-400/10 text-amber-200 hover:bg-amber-400/10'
  }

  return 'rounded-full border border-emerald-400/25 bg-emerald-400/10 text-emerald-200 hover:bg-emerald-400/10'
}

function StatusMetric({
  icon: Icon,
  label,
  value,
  helper,
}: {
  icon: typeof Activity
  label: string
  value: string
  helper: string
}) {
  return (
    <div className="rounded-2xl border border-border/60 bg-background/25 p-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <Icon className="size-4 text-primary" />
      </div>
      <p className="mt-3 text-sm font-medium">{value}</p>
      <p className="mt-1 text-xs leading-5 text-muted-foreground">{helper}</p>
    </div>
  )
}
