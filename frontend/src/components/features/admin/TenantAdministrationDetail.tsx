import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { ArrowLeft, Clock3, KeyRound, Landmark } from 'lucide-react'
import { updateTenant } from '@/api/settings.functions'
import type { TenantDetail, TenantIngestionSource } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

type TenantAdministrationDetailProps = {
  tenant: TenantDetail
}

export function TenantAdministrationDetail({ tenant }: TenantAdministrationDetailProps) {
  const [name, setName] = useState(tenant.name)
  const [sources, setSources] = useState(() => tenant.ingestionSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')

  const mutation = useMutation({
    mutationFn: async () => {
      await updateTenant({
        data: {
          tenantId: tenant.id,
          name: name.trim(),
          ingestionSources: sources.map((source) => ({
            key: source.key,
            displayName: source.displayName,
            enabled: source.enabled,
            syncSchedule: source.syncSchedule,
            credentials: {
              tenantId: source.credentials.tenantId,
              clientId: source.credentials.clientId,
              clientSecret: source.credentials.clientSecret,
              apiBaseUrl: source.credentials.apiBaseUrl,
              tokenScope: source.credentials.tokenScope,
            },
          })),
        },
      })
    },
    onSuccess: () => {
      setSaveState('saved')
    },
    onError: () => {
      setSaveState('error')
    },
  })

  const configuredSources = sources.filter((source) => {
    const credentials = source.credentials
    return Boolean(credentials.tenantId || credentials.clientId || credentials.hasClientSecret)
  }).length

  function updateSource(
    key: string,
    mutate: (current: TenantIngestionSourceDraft) => TenantIngestionSourceDraft,
  ) {
    setSaveState('idle')
    setSources((current) => current.map((source) => (source.key === key ? mutate(source) : source)))
  }

  return (
    <section className="space-y-4 pb-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-2">
          <Link to="/admin/tenants" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" />
            Back to tenants
          </Link>
          <div>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">{tenant.name}</h1>
            <p className="mt-1 text-sm text-muted-foreground">Manage tenant naming, ingestion credentials, and sync schedules.</p>
          </div>
        </div>
        <Button
          onClick={() => {
            mutation.mutate()
          }}
          disabled={mutation.isPending || name.trim().length === 0}
        >
          {mutation.isPending ? 'Saving...' : 'Save tenant'}
        </Button>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)]">
        <Card className="rounded-[28px] border-border/70 bg-card/82">
          <CardHeader>
            <CardTitle>Tenant Identity</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Tenant Name</span>
              <Input value={name} onChange={(event) => setName(event.target.value)} />
            </label>
            <div className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Entra Tenant ID</span>
              <div className="rounded-xl border border-input bg-input/30 px-3 py-2 text-sm text-muted-foreground">
                {tenant.entraTenantId}
              </div>
            </div>
          </CardContent>
        </Card>

        <Card className="rounded-[28px] border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_88%,black),var(--card))]">
          <CardHeader>
            <CardTitle>Administration Snapshot</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-3 xl:grid-cols-1">
            <SnapshotRow icon={Landmark} label="Tenant ID" value={tenant.id} />
            <SnapshotRow icon={KeyRound} label="Configured Sources" value={String(configuredSources)} />
            <SnapshotRow icon={Clock3} label="Source Cards" value={String(sources.length)} />
            {saveState === 'saved' ? <p className="text-sm text-emerald-300">Tenant configuration saved.</p> : null}
            {saveState === 'error' ? <p className="text-sm text-destructive">Save failed. Try again.</p> : null}
          </CardContent>
        </Card>
      </div>

      <div className="space-y-4">
        {sources.map((source) => {
          const isConfigured = Boolean(
            source.credentials.tenantId || source.credentials.clientId || source.credentials.hasClientSecret,
          )

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
                    </div>
                    <p className="mt-1 text-sm text-muted-foreground">
                      Configure API credentials and the schedule string used for sync orchestration.
                    </p>
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
              </CardHeader>
              <CardContent className="space-y-5">
                <label className="flex items-center gap-3 rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                  <input
                    type="checkbox"
                    checked={source.enabled}
                    onChange={(event) => {
                      updateSource(source.key, (current) => ({
                        ...current,
                        enabled: event.target.checked,
                      }))
                    }}
                  />
                  <div>
                    <p className="text-sm font-medium">Enable source</p>
                    <p className="text-xs text-muted-foreground">Included in tenant ingestion schedule and credential validation.</p>
                  </div>
                </label>

                <div className="grid gap-4 md:grid-cols-2">
                  <label className="space-y-2">
                    <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Display Name</span>
                    <Input
                      value={source.displayName}
                      onChange={(event) => {
                        updateSource(source.key, (current) => ({
                          ...current,
                          displayName: event.target.value,
                        }))
                      }}
                    />
                  </label>
                  <label className="space-y-2">
                    <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Sync Schedule</span>
                    <Input
                      value={source.syncSchedule}
                      onChange={(event) => {
                        updateSource(source.key, (current) => ({
                          ...current,
                          syncSchedule: event.target.value,
                        }))
                      }}
                    />
                  </label>
                </div>

                <div className="grid gap-4 md:grid-cols-2">
                  <label className="space-y-2">
                    <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Credential Tenant ID</span>
                    <Input
                      value={source.credentials.tenantId}
                      onChange={(event) => {
                        updateSource(source.key, (current) => ({
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
                        updateSource(source.key, (current) => ({
                          ...current,
                          credentials: { ...current.credentials, clientId: event.target.value },
                        }))
                      }}
                    />
                  </label>
                  <label className="space-y-2 md:col-span-2">
                    <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Client Secret</span>
                    <Input
                      type="password"
                      value={source.credentials.clientSecret}
                      placeholder={source.credentials.hasClientSecret ? 'Stored in OpenBao. Enter a new value to rotate.' : 'Not configured'}
                      onChange={(event) => {
                        updateSource(source.key, (current) => ({
                          ...current,
                          credentials: {
                            ...current.credentials,
                            clientSecret: event.target.value,
                            hasClientSecret: current.credentials.hasClientSecret || event.target.value.trim().length > 0,
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
                        updateSource(source.key, (current) => ({
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
                        updateSource(source.key, (current) => ({
                          ...current,
                          credentials: { ...current.credentials, tokenScope: event.target.value },
                        }))
                      }}
                    />
                  </label>
                </div>
              </CardContent>
            </Card>
          )
        })}
      </div>
    </section>
  )
}

type TenantIngestionSourceDraft = Omit<TenantIngestionSource, 'credentials'> & {
  credentials: TenantIngestionSource['credentials'] & {
    clientSecret: string
  }
}

function mapSourceToDraft(source: TenantIngestionSource): TenantIngestionSourceDraft {
  return {
    ...source,
    credentials: {
      ...source.credentials,
      clientSecret: '',
    },
  }
}

type SnapshotRowProps = {
  icon: typeof Landmark
  label: string
  value: string
}

function SnapshotRow({ icon: Icon, label, value }: SnapshotRowProps) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background/35 p-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <Icon className="size-4 text-primary" />
      </div>
      <p className="mt-3 break-all text-sm font-medium text-foreground">{value}</p>
    </div>
  )
}
