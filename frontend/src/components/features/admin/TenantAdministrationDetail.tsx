import { useState } from 'react'
import { Link, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { toast } from 'sonner'
import { ArrowLeft, CircleHelp, Landmark } from 'lucide-react'
import { updateTenant } from '@/api/settings.functions'
import type { TenantDetail } from '@/api/settings.schemas'
import type { AuditLogItem } from '@/api/audit-log.schemas'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Separator } from '@/components/ui/separator'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'

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
      toast.success('Tenant configuration saved')
      void router.invalidate()
    },
    onError: () => {
      setSaveState('error')
      toast.error('Failed to save tenant configuration')
    },
  })

  return (
    <TooltipProvider>
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
        <Card className="rounded-2xl bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_94%,black),var(--card))]">
          <CardHeader className="border-b border-border/60 pb-5">
            <CardTitle>Tenant Identity</CardTitle>
          </CardHeader>
          <CardContent className="space-y-6 pt-6">
            <FormSection
              title="Identity"
              description="Update the tenant display name while keeping the linked Entra tenant reference visible as read-only context."
            >
              <div className="grid gap-5 sm:grid-cols-2">
                <FieldBlock
                  label="Tenant Name"
                  tooltip="The primary display name shown throughout the app for this tenant."
                  control={(
                    <Input
                    value={name}
                    onChange={(event) => setName(event.target.value)}
                    className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                  />
                  )}
                />
                <FieldBlock
                  label="Entra Tenant ID"
                  tooltip="Read-only identity used to link the tenant to Microsoft Entra."
                  control={(
                    <div className="rounded-lg border border-border/70 bg-muted/55 px-3 py-3 text-sm text-muted-foreground">
                      {tenant.entraTenantId}
                    </div>
                  )}
                />
              </div>
            </FormSection>
          </CardContent>
        </Card>

        <Card className="rounded-2xl border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_88%,black),var(--card))]">
          <CardHeader>
            <CardTitle>Tenant Assets</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <SnapshotRow icon={Landmark} label="Tenant Name" value={tenant.name} />
            <InsetPanel className="p-4">
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
            </InsetPanel>
            <InsetPanel className="p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Sources</p>
              <p className="mt-1 text-sm text-muted-foreground">
                Source configuration now lives in the dedicated admin sources view.
              </p>
              <Link to="/admin/sources" className="mt-3 inline-flex text-sm font-medium text-primary hover:underline">
                Open sources administration
              </Link>
            </InsetPanel>
            {saveState === 'saved' ? <p className="text-sm text-tone-success-foreground">Tenant configuration saved.</p> : null}
            {saveState === 'error' ? <p className="text-sm text-destructive">Save failed. Try again.</p> : null}
          </CardContent>
        </Card>
      </div>

      <Card className="rounded-2xl">
        <CardHeader className="border-b border-border/60 pb-5">
          <CardTitle>SLA Configuration</CardTitle>
        </CardHeader>
        <CardContent className="space-y-6 pt-6">
            <FormSection
              title="Response Targets"
              description="Define the remediation target window, in days, for each severity band."
            >
              <div className="grid gap-5 md:grid-cols-4">
                <FieldBlock
                  label="Critical"
                  tooltip="Target remediation window in days for critical findings."
                  control={(
                    <Input
                  value={sla.criticalDays}
                  onChange={(event) => setSla((current) => ({ ...current, criticalDays: event.target.value }))}
                  className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                />
                  )}
                />
                <FieldBlock
                  label="High"
                  tooltip="Target remediation window in days for high severity findings."
                  control={(
                    <Input
                  value={sla.highDays}
                  onChange={(event) => setSla((current) => ({ ...current, highDays: event.target.value }))}
                  className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                />
                  )}
                />
                <FieldBlock
                  label="Medium"
                  tooltip="Target remediation window in days for medium severity findings."
                  control={(
                    <Input
                  value={sla.mediumDays}
                  onChange={(event) => setSla((current) => ({ ...current, mediumDays: event.target.value }))}
                  className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                />
                  )}
                />
                <FieldBlock
                  label="Low"
                  tooltip="Target remediation window in days for low severity findings."
                  control={(
                    <Input
                  value={sla.lowDays}
                  onChange={(event) => setSla((current) => ({ ...current, lowDays: event.target.value }))}
                  className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]"
                />
                  )}
                />
              </div>
            </FormSection>
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
    </TooltipProvider>
  )
}

type SnapshotRowProps = {
  icon: typeof Landmark
  label: string
  value: string
}

function SnapshotRow({ icon: Icon, label, value }: SnapshotRowProps) {
  return (
    <InsetPanel className="p-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <Icon className="size-4 text-primary" />
      </div>
      <p className="mt-3 break-all text-sm font-medium text-foreground">{value}</p>
    </InsetPanel>
  )
}

function AssetCountRow({ label, count }: { label: string; count: number }) {
  return (
    <InsetPanel emphasis="strong" className="flex items-center justify-between gap-3 px-3 py-2">
      <span className="text-sm font-medium text-foreground">{label}</span>
      <Badge variant="outline" className="rounded-full border-border/70 bg-background/70 text-foreground">
        {count}
      </Badge>
    </InsetPanel>
  )
}

function FormSection({
  title,
  description,
  children,
}: {
  title: string
  description: string
  children: React.ReactNode
}) {
  return (
    <div className="space-y-5">
      <div className="space-y-1">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{title}</p>
        <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{description}</p>
      </div>
      {children}
      <Separator className="opacity-60" />
    </div>
  )
}

function FieldBlock({
  label,
  tooltip,
  control,
}: {
  label: string
  tooltip?: string
  control: React.ReactNode
}) {
  return (
    <div className="grid content-start gap-2">
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

function isPositiveInteger(value: string) {
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed > 0
}
