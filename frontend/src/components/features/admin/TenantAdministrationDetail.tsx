import { useState } from 'react'
import { Link, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { ArrowLeft, Landmark } from 'lucide-react'
import { updateTenant } from '@/api/settings.functions'
import type { TenantDetail } from '@/api/settings.schemas'
import type { AuditLogItem } from '@/api/audit-log.schemas'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

type TenantAdministrationDetailProps = {
  tenant: TenantDetail
  canViewAudit?: boolean
  recentAuditItems?: AuditLogItem[]
}

export function TenantAdministrationDetail({
  tenant,
  canViewAudit = false,
  recentAuditItems = [],
}: TenantAdministrationDetailProps) {
  const router = useRouter()
  const [name, setName] = useState(tenant.name)
  const [sla, setSla] = useState({
    criticalDays: String(tenant.sla.criticalDays),
    highDays: String(tenant.sla.highDays),
    mediumDays: String(tenant.sla.mediumDays),
    lowDays: String(tenant.sla.lowDays),
  })
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')

  const mutation = useMutation({
    mutationFn: async () => {
      await updateTenant({
        data: {
          tenantId: tenant.id,
          name: name.trim(),
          sla: {
            criticalDays: Number(sla.criticalDays),
            highDays: Number(sla.highDays),
            mediumDays: Number(sla.mediumDays),
            lowDays: Number(sla.lowDays),
          },
          ingestionSources: tenant.ingestionSources.map((source) => ({
            key: source.key,
            displayName: source.displayName,
            enabled: source.enabled,
            syncSchedule: source.syncSchedule,
            credentials: {
              tenantId: source.credentials.tenantId,
              clientId: source.credentials.clientId,
              secret: '',
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

  return (
    <section className="space-y-4 pb-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-2">
          <Link
            to="/admin/tenants"
            search={{ page: 1, pageSize: 25 }}
            className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
          >
            <ArrowLeft className="size-4" />
            Back to tenants
          </Link>
          <div>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">{tenant.name}</h1>
            <p className="mt-1 text-sm text-muted-foreground">Manage tenant identity and review inventory footprint.</p>
          </div>
        </div>
        <Button
          onClick={() => mutation.mutate()}
          disabled={
            mutation.isPending
            || name.trim().length === 0
            || !isPositiveInteger(sla.criticalDays)
            || !isPositiveInteger(sla.highDays)
            || !isPositiveInteger(sla.mediumDays)
            || !isPositiveInteger(sla.lowDays)
          }
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
          <CardContent className="space-y-4">
            <SnapshotRow icon={Landmark} label="Tenant ID" value={tenant.id} />
            <div className="rounded-2xl border border-border/70 bg-background/35 p-4">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Inventory</p>
                  <p className="mt-1 text-sm text-muted-foreground">Current asset footprint for this tenant.</p>
                </div>
                <Badge variant="outline" className="rounded-full border-primary/30 bg-primary/10 text-primary">
                  {tenant.assets.totalCount} total
                </Badge>
              </div>
              <div className="mt-4 space-y-2">
                <AssetCountRow label="Devices" count={tenant.assets.deviceCount} />
                <AssetCountRow label="Software" count={tenant.assets.softwareCount} />
                <AssetCountRow label="Cloud Resources" count={tenant.assets.cloudResourceCount} />
              </div>
            </div>
            <div className="rounded-2xl border border-border/70 bg-background/35 p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Sources</p>
              <p className="mt-1 text-sm text-muted-foreground">
                Source configuration now lives in the dedicated admin sources view.
              </p>
              <Link to="/admin/sources" className="mt-3 inline-flex text-sm font-medium text-primary hover:underline">
                Open sources administration
              </Link>
            </div>
            {saveState === 'saved' ? <p className="text-sm text-emerald-300">Tenant configuration saved.</p> : null}
            {saveState === 'error' ? <p className="text-sm text-destructive">Save failed. Try again.</p> : null}
          </CardContent>
        </Card>
      </div>

      <Card className="rounded-[28px] border-border/70 bg-card/82">
        <CardHeader>
          <CardTitle>SLA Configuration</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-4">
          <label className="space-y-2">
            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Critical</span>
            <Input
              value={sla.criticalDays}
              onChange={(event) => setSla((current) => ({ ...current, criticalDays: event.target.value }))}
            />
          </label>
          <label className="space-y-2">
            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">High</span>
            <Input
              value={sla.highDays}
              onChange={(event) => setSla((current) => ({ ...current, highDays: event.target.value }))}
            />
          </label>
          <label className="space-y-2">
            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Medium</span>
            <Input
              value={sla.mediumDays}
              onChange={(event) => setSla((current) => ({ ...current, mediumDays: event.target.value }))}
            />
          </label>
          <label className="space-y-2">
            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Low</span>
            <Input
              value={sla.lowDays}
              onChange={(event) => setSla((current) => ({ ...current, lowDays: event.target.value }))}
            />
          </label>
        </CardContent>
      </Card>

      {canViewAudit ? (
        <RecentAuditPanel
          title="Tenant Activity"
          description="Recent tenant-level configuration changes, including identity, SLA, and tenant-scoped configuration updates."
          items={recentAuditItems}
          emptyMessage="No recent tenant configuration changes have been recorded."
        />
      ) : null}
    </section>
  )
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

function AssetCountRow({ label, count }: { label: string; count: number }) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-xl border border-border/60 bg-card/40 px-3 py-2">
      <span className="text-sm font-medium text-foreground">{label}</span>
      <Badge variant="outline" className="rounded-full border-border/70 bg-background/70 text-foreground">
        {count}
      </Badge>
    </div>
  )
}

function isPositiveInteger(value: string) {
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed > 0
}
