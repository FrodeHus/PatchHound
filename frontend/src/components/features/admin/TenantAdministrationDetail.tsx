import { useState } from 'react'
import { Link, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import {
  AlertTriangle,
  ArrowLeft,
  Bot,
  CircleHelp,
  DatabaseZap,
  GitBranchPlus,
  Landmark,
  type Shield,
  Tags,
  Trash2,
  Workflow,
} from 'lucide-react'
import { deleteTenant, fetchTenantDetail, updateTenant } from '@/api/settings.functions'
import type { TenantDetail } from '@/api/settings.schemas'
import type { AuditLogItem } from '@/api/audit-log.schemas'
import { TenantSourceManagement } from '@/components/features/admin/TenantSourceManagement'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { TenantAiSettingsPage } from '@/components/features/settings/TenantAiSettingsPage'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Separator } from '@/components/ui/separator'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import { getApiErrorMessage } from '@/lib/api-errors'

type TabId = 'overview' | 'sources' | 'ai-settings' | 'device-rules' | 'workflows' | 'business-labels'

type TabSearch = {
  mode?: string
  sourceKey?: string
  profileId?: string
}

type TenantAdministrationDetailProps = {
  tenant: TenantDetail
  canViewAudit?: boolean
  recentAuditItems?: AuditLogItem[]
  activeTab?: TabId
  tabSearch?: TabSearch
  onTabChange?: (tab: TabId) => void
  onSearchChange?: (patch: Partial<TabSearch & { mode?: string }>) => void
}

export function TenantAdministrationDetail({
  tenant,
  canViewAudit = false,
  recentAuditItems = [],
  activeTab = 'overview',
  tabSearch = {},
  onTabChange,
  onSearchChange,
}: TenantAdministrationDetailProps) {
  return (
    <TooltipProvider>
      <section className="space-y-4 pb-4">
        {/* Header */}
        <div className="space-y-2">
          <Link
            to="/admin/tenants"
            search={{ page: 1, pageSize: 25 }}
            className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
          >
            <ArrowLeft className="size-4" />
            Back to tenants
          </Link>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-3xl font-semibold tracking-[-0.04em]">{tenant.name}</h1>
              {tenant.isPrimary && (
                <Badge variant="secondary" className="rounded-full">
                  Primary
                </Badge>
              )}
            </div>
          </div>
          <p className="text-sm text-muted-foreground">
            Configure identity, sources, AI profiles, and automation for this tenant.
          </p>
        </div>

        {/* Tab hub */}
        <Tabs
          value={activeTab}
          onValueChange={(value) => onTabChange?.(value as TabId)}
          className="space-y-0"
        >
          <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
            <TabsTrigger value="overview" className="rounded-lg px-4 text-sm">Overview</TabsTrigger>
            <TabsTrigger value="sources" className="rounded-lg px-4 text-sm">
              <DatabaseZap className="size-3.5" />
              Sources
            </TabsTrigger>
            <TabsTrigger value="ai-settings" className="rounded-lg px-4 text-sm">
              <Bot className="size-3.5" />
              AI Settings
            </TabsTrigger>
            <TabsTrigger value="device-rules" className="rounded-lg px-4 text-sm">
              <GitBranchPlus className="size-3.5" />
              Device Rules
            </TabsTrigger>
            <TabsTrigger value="workflows" className="rounded-lg px-4 text-sm">
              <Workflow className="size-3.5" />
              Workflows
            </TabsTrigger>
            <TabsTrigger value="business-labels" className="rounded-lg px-4 text-sm">
              <Tags className="size-3.5" />
              Business Labels
            </TabsTrigger>
          </TabsList>

          <TabsContent value="overview" className="space-y-4 pt-4">
            <OverviewTab
              tenant={tenant}
              canViewAudit={canViewAudit}
              recentAuditItems={recentAuditItems}
            />
          </TabsContent>

          <TabsContent value="sources" className="pt-4">
            <SourcesTab
              tenant={tenant}
              mode={tabSearch.mode}
              sourceKey={tabSearch.sourceKey}
              onSearchChange={onSearchChange}
            />
          </TabsContent>

          <TabsContent value="ai-settings" className="pt-4">
            <TenantAiSettingsPage
              tenantId={tenant.id}
              tenantName={tenant.name}
              mode={tabSearch.mode ?? null}
              profileId={tabSearch.profileId ?? null}
              onSearchChange={(patch) => onSearchChange?.({ mode: patch.mode, profileId: patch.profileId })}
              onClearSearch={() => onSearchChange?.({ mode: undefined, profileId: undefined })}
            />
          </TabsContent>

          <TabsContent value="device-rules" className="pt-4">
            <LinkedTabStub
              icon={GitBranchPlus}
              title="Device Rules"
              description="Automate ownership and security-profile assignment based on device conditions."
              linkTo="/admin/device-rules"
              linkLabel="Open device rules"
            />
          </TabsContent>

          <TabsContent value="workflows" className="pt-4">
            <LinkedTabStub
              icon={Workflow}
              title="Workflows"
              description="Design and manage triage, assignment, and approval workflows."
              linkTo="/admin/workflows"
              linkLabel="Open workflows"
            />
          </TabsContent>

          <TabsContent value="business-labels" className="pt-4">
            <LinkedTabStub
              icon={Tags}
              title="Business Labels"
              description="Define business labels like Production or Finance and apply them to assets."
              linkTo="/admin/business-labels"
              linkLabel="Open business labels"
            />
          </TabsContent>
        </Tabs>
      </section>
    </TooltipProvider>
  )
}

// ─── Overview tab ────────────────────────────────────────────────────────────

function OverviewTab({
  tenant,
  canViewAudit,
  recentAuditItems,
}: {
  tenant: TenantDetail
  canViewAudit: boolean
  recentAuditItems: AuditLogItem[]
}) {
  const router = useRouter()
  const [name, setName] = useState(tenant.name)
  const [sla, setSla] = useState({
    criticalDays: String(tenant.sla.criticalDays),
    highDays: String(tenant.sla.highDays),
    mediumDays: String(tenant.sla.mediumDays),
    lowDays: String(tenant.sla.lowDays),
  })
  const [saveState, setSaveState] = useState<'idle' | 'saved' | 'error'>('idle')
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [deleteConfirmation, setDeleteConfirmation] = useState('')

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
    onError: (error) => {
      setSaveState('error')
      toast.error(getApiErrorMessage(error, 'Failed to save tenant configuration'))
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async () => {
      await deleteTenant({ data: { tenantId: tenant.id } })
    },
    onSuccess: async () => {
      toast.success('Tenant deletion queued. You will be notified when complete.')
      setDeleteDialogOpen(false)
      setDeleteConfirmation('')
      await router.invalidate()
      await router.navigate({ to: '/admin/tenants', search: { page: 1, pageSize: 25 } })
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to delete tenant'))
    },
  })

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
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

      <Card className="rounded-2xl border-destructive/30 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--destructive)_6%,var(--card)),var(--card))]">
        <CardHeader className="border-b border-destructive/20 pb-5">
          <CardTitle>Danger Zone</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4 pt-6">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="max-w-2xl space-y-2">
              <div className="flex items-center gap-2 text-destructive">
                <AlertTriangle className="size-4" />
                <p className="font-medium">Delete this tenant</p>
              </div>
              <p className="text-sm text-muted-foreground">
                This permanently removes the tenant, its inventory, remediation data, workflows, audit log entries, tenant-scoped users, and stored tenant secrets. This action cannot be undone.
              </p>
            </div>
            {tenant.isPrimary ? (
              <Tooltip>
                <TooltipTrigger className="cursor-not-allowed">
                  <Button variant="destructive" disabled>
                    <Trash2 className="mr-2 size-4" />
                    Delete tenant
                  </Button>
                </TooltipTrigger>
                <TooltipContent className="max-w-xs">
                  The primary tenant cannot be deleted. It is used for login and global access management.
                </TooltipContent>
              </Tooltip>
            ) : (
              <Dialog open={deleteDialogOpen} onOpenChange={(open) => {
                setDeleteDialogOpen(open)
                if (!open) setDeleteConfirmation('')
              }}>
                <DialogTrigger render={<Button variant="destructive" />}>
                  <Trash2 className="mr-2 size-4" />
                  Delete tenant
                </DialogTrigger>
                <DialogContent>
                  <DialogHeader>
                    <DialogTitle>Delete {tenant.name}?</DialogTitle>
                    <DialogDescription>
                      This permanently deletes all tenant-scoped data and tenant-owned secrets. Type the tenant name exactly to confirm.
                    </DialogDescription>
                  </DialogHeader>
                  <div className="space-y-3 py-2">
                    <div className="rounded-xl border border-destructive/20 bg-destructive/5 p-3 text-sm text-muted-foreground">
                      <p>Tenant: <span className="font-medium text-foreground">{tenant.name}</span></p>
                      <p>Assets: <span className="font-medium text-foreground">{tenant.assets.totalCount}</span></p>
                      <p>Configured sources: <span className="font-medium text-foreground">{tenant.ingestionSources.length}</span></p>
                    </div>
                    <div className="space-y-1.5">
                      <label htmlFor="tenant-delete-confirmation" className="text-sm font-medium">
                        Type <span className="font-mono">{tenant.name}</span> to confirm
                      </label>
                      <Input
                        id="tenant-delete-confirmation"
                        value={deleteConfirmation}
                        onChange={(event) => setDeleteConfirmation(event.target.value)}
                        autoComplete="off"
                      />
                    </div>
                  </div>
                  <DialogFooter>
                    <DialogClose render={<Button variant="outline" />}>
                      Cancel
                    </DialogClose>
                    <Button
                      variant="destructive"
                      disabled={deleteConfirmation.trim() !== tenant.name || deleteMutation.isPending}
                      onClick={() => deleteMutation.mutate()}
                    >
                      {deleteMutation.isPending ? 'Deleting...' : 'Permanently delete tenant'}
                    </Button>
                  </DialogFooter>
                </DialogContent>
              </Dialog>
            )}
          </div>
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
    </div>
  )
}

// ─── Sources tab ─────────────────────────────────────────────────────────────

function SourcesTab({
  tenant,
  mode,
  sourceKey,
  onSearchChange,
}: {
  tenant: TenantDetail
  mode?: string
  sourceKey?: string
  onSearchChange?: (patch: Partial<TabSearch>) => void
}) {
  const tenantQuery = useQuery({
    queryKey: ['tenant-detail', tenant.id],
    queryFn: () => fetchTenantDetail({ data: { tenantId: tenant.id } }),
    initialData: tenant,
    refetchInterval: (query) => {
      const data = query.state.data
      if (!data) return false
      return data.ingestionSources.some((source) => source.runtime.activeIngestionRunId) ? 3000 : false
    },
  })

  const liveTenant = tenantQuery.data ?? tenant

  return (
    <TenantSourceManagement
      key={liveTenant.id}
      tenant={liveTenant}
      editingSourceKey={mode === 'edit' ? (sourceKey ?? null) : null}
      historySourceKey={mode === 'history' ? (sourceKey ?? null) : null}
      onEditSource={(key) => onSearchChange?.({ mode: 'edit', sourceKey: key })}
      onOpenHistory={(key) => onSearchChange?.({ mode: 'history', sourceKey: key })}
      onCloseEditor={() => onSearchChange?.({ mode: undefined, sourceKey: undefined })}
      onCloseHistory={() => onSearchChange?.({ mode: undefined, sourceKey: undefined })}
    />
  )
}

// ─── Stub for not-yet-embedded tabs ──────────────────────────────────────────

function LinkedTabStub({
  icon: Icon,
  title,
  description,
  linkTo,
  linkLabel,
}: {
  icon: typeof Shield
  title: string
  description: string
  linkTo: string
  linkLabel: string
}) {
  return (
    <Card className="rounded-2xl border-border/70 bg-card/85">
      <CardContent className="flex flex-col items-start gap-4 py-8">
        <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
          <Icon className="size-5 text-primary" />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{title}</p>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <Link to={linkTo as never} className="text-sm font-medium text-primary hover:underline">
          {linkLabel}
        </Link>
      </CardContent>
    </Card>
  )
}

// ─── Shared sub-components ────────────────────────────────────────────────────

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
