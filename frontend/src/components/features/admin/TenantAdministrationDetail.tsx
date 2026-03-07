import { useState } from 'react'
import { Link, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { ArrowLeft, Boxes, HardDrive, Landmark, Package, RotateCw, ServerCog } from 'lucide-react'
import { triggerTenantIngestionSync, updateTenant } from '@/api/settings.functions'
import type { TenantDetail, TenantIngestionSource } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

type TenantAdministrationDetailProps = {
  tenant: TenantDetail
}

export function TenantAdministrationDetail({ tenant }: TenantAdministrationDetailProps) {
  const router = useRouter()
  const [name, setName] = useState(tenant.name)
  const [sources, setSources] = useState(() => tenant.ingestionSources.map(mapSourceToDraft))
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [syncingSourceKey, setSyncingSourceKey] = useState<string | null>(null)
  const [syncState, setSyncState] = useState<'idle' | 'success' | 'error'>('idle')

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
            <CardTitle>Tenant Assets</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-2 xl:grid-cols-1">
            <SnapshotRow icon={Landmark} label="Tenant ID" value={tenant.id} />
            <SnapshotRow icon={Boxes} label="Total Assets" value={String(tenant.assets.totalCount)} />
            <SnapshotRow icon={ServerCog} label="Devices" value={String(tenant.assets.deviceCount)} />
            <SnapshotRow icon={Package} label="Software" value={String(tenant.assets.softwareCount)} />
            <SnapshotRow icon={HardDrive} label="Cloud Resources" value={String(tenant.assets.cloudResourceCount)} />
            {saveState === 'saved' ? <p className="text-sm text-emerald-300">Tenant configuration saved.</p> : null}
            {saveState === 'error' ? <p className="text-sm text-destructive">Save failed. Try again.</p> : null}
            {syncState === 'success' ? <p className="text-sm text-emerald-300">Ingestion sync started.</p> : null}
            {syncState === 'error' ? <p className="text-sm text-destructive">Sync trigger failed. Try again.</p> : null}
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
                <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                  <span>Last ingestion: {formatTimestamp(source.runtime.lastCompletedAt)}</span>
                  {source.runtime.lastStatus ? (
                    <Badge variant="outline" className="rounded-full border-border/70 bg-background/60 text-muted-foreground">
                      {source.runtime.lastStatus}
                    </Badge>
                  ) : null}
                </div>
              </CardHeader>
              <CardContent className="space-y-5">
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
                    onClick={() => {
                      syncMutation.mutate(source.key)
                    }}
                  >
                    <RotateCw className="size-4" />
                    {syncingSourceKey === source.key ? 'Syncing...' : 'Run sync now'}
                  </Button>
                </div>

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
