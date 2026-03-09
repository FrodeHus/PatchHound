import { Link } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { ArrowRight, BadgeCheck, Building2, DatabaseZap, KeyRound, ShieldCheck } from 'lucide-react'
import type { TenantListItem } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { PaginationControls } from '@/components/ui/pagination-controls'
import {
  DataTableEmptyState,
  DataTableSummaryStrip,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'

type TenantAdministrationListProps = {
  tenants: TenantListItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  isCreating: boolean
  createError: string | null
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onCreate: (payload: { name: string; entraTenantId: string }) => Promise<unknown>
}

export function TenantAdministrationList({
  tenants,
  totalCount,
  page,
  pageSize,
  totalPages,
  isCreating,
  createError,
  onPageChange,
  onPageSizeChange,
  onCreate,
}: TenantAdministrationListProps) {
  const [name, setName] = useState('')
  const [entraTenantId, setEntraTenantId] = useState('')
  const summaryItems = useMemo(
    () => [
      { label: 'Tenants on page', value: tenants.length.toString(), tone: 'accent' as const },
      {
        label: 'Configured sources',
        value: tenants.reduce((sum, tenant) => sum + tenant.configuredIngestionSourceCount, 0).toString(),
      },
      {
        label: 'Ready to connect',
        value: tenants.filter((tenant) => tenant.configuredIngestionSourceCount === 0).length.toString(),
      },
    ],
    [tenants],
  )

  const columns = useMemo<ColumnDef<TenantListItem>[]>(
    () => [
      {
        accessorKey: 'name',
        header: 'Tenant',
        cell: ({ row }) => (
          <div className="space-y-1">
            <Link
              to="/admin/tenants/$id"
              params={{ id: row.original.id }}
              className="font-medium tracking-tight underline decoration-border/70 underline-offset-4 transition hover:decoration-foreground"
            >
              {row.original.name}
            </Link>
            <p className="font-mono text-[11px] text-muted-foreground">{row.original.entraTenantId}</p>
          </div>
        ),
      },
      {
        accessorKey: 'entraTenantId',
        header: 'Entra tenant ID',
        cell: ({ row }) => <span className="font-mono text-xs text-muted-foreground">{row.original.entraTenantId}</span>,
      },
      {
        accessorKey: 'configuredIngestionSourceCount',
        header: 'Configured sources',
        cell: ({ row }) => (
          <Badge variant="outline" className="rounded-full border-border/70 bg-background/70 text-foreground">
            {row.original.configuredIngestionSourceCount}
          </Badge>
        ),
      },
      {
        id: 'open',
        header: () => <div className="text-right">Open</div>,
        cell: ({ row }) => (
          <div className="text-right">
            <Link
              to="/admin/tenants/$id"
              params={{ id: row.original.id }}
              className="text-sm font-medium text-primary underline decoration-primary/30 underline-offset-4 transition hover:decoration-primary"
            >
              Open detail
            </Link>
          </div>
        ),
      },
    ],
    [],
  )

  return (
    <section className="space-y-4">
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <Card className="rounded-[28px] border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_90%,black),var(--card))]">
          <CardHeader>
            <Badge variant="outline" className="w-fit rounded-full border-primary/20 bg-primary/10 text-primary">
              Tenant Administration
            </Badge>
            <CardTitle className="mt-3 text-3xl font-semibold tracking-[-0.04em]">
              Direct control over tenant identity, source credentials, and sync policy.
            </CardTitle>
          </CardHeader>
          <CardContent className="pt-0 text-sm leading-6 text-muted-foreground">
            Keep tenant naming clean, confirm which ingestion connections are configured, and move from tenant directory to source-level editing without raw JSON.
          </CardContent>
        </Card>

        <div className="grid gap-4 sm:grid-cols-3 lg:grid-cols-1">
          <Card className="rounded-[28px] border-border/70 bg-card/80">
            <CardHeader>
              <div className="flex items-center justify-between">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Tenants</p>
                <Building2 className="size-4 text-primary" />
              </div>
              <CardTitle className="text-3xl font-semibold tracking-[-0.04em]">{totalCount}</CardTitle>
            </CardHeader>
          </Card>
          <Card className="rounded-[28px] border-border/70 bg-card/80">
            <CardHeader>
              <div className="flex items-center justify-between">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Configured Sources</p>
                <KeyRound className="size-4 text-primary" />
              </div>
              <CardTitle className="text-3xl font-semibold tracking-[-0.04em]">
                {tenants.reduce((sum, tenant) => sum + tenant.configuredIngestionSourceCount, 0)}
              </CardTitle>
            </CardHeader>
          </Card>
          <Card className="rounded-[28px] border-border/70 bg-card/80">
            <CardHeader>
              <div className="flex items-center justify-between">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Ready To Connect</p>
                <DatabaseZap className="size-4 text-primary" />
              </div>
              <CardTitle className="text-3xl font-semibold tracking-[-0.04em]">
                {tenants.filter((tenant) => tenant.configuredIngestionSourceCount === 0).length}
              </CardTitle>
            </CardHeader>
          </Card>
        </div>
      </div>

      <Card className="rounded-[28px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_9%,transparent),transparent_48%),var(--color-card)]">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="space-y-2">
              <Badge variant="outline" className="w-fit rounded-full border-primary/20 bg-primary/10 text-primary">
                Add New Tenant
              </Badge>
              <CardTitle className="text-2xl font-semibold tracking-[-0.04em]">
                Register a tenant, seed the defaults, then finish the connection in sources.
              </CardTitle>
              <p className="max-w-3xl text-sm leading-6 text-muted-foreground">
                You only need the tenant display name and the Microsoft Entra tenant ID. PatchHound will create the
                tenant record, default SLA policy, and a disabled Microsoft Defender source template automatically.
              </p>
            </div>
            <div className="grid min-w-[16rem] gap-2 text-sm text-muted-foreground">
              <GuideItem
                icon={BadgeCheck}
                label="Required now"
                text="Tenant name and Entra tenant ID."
              />
              <GuideItem
                icon={ShieldCheck}
                label="Finish after create"
                text="Open Sources to add Defender app credentials and enable sync."
              />
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-5 lg:grid-cols-[minmax(0,1.4fr)_minmax(18rem,0.8fr)]">
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Tenant Name</span>
              <Input
                placeholder="Contoso Production"
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
            </label>
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Entra Tenant ID</span>
              <Input
                placeholder="00000000-0000-0000-0000-000000000000"
                value={entraTenantId}
                onChange={(event) => setEntraTenantId(event.target.value)}
              />
            </label>
            <div className="sm:col-span-2 flex flex-wrap items-center gap-3">
              <Button
                disabled={isCreating || name.trim().length === 0 || entraTenantId.trim().length === 0}
                onClick={() => {
                  void (async () => {
                    try {
                      await onCreate({
                        name: name.trim(),
                        entraTenantId: entraTenantId.trim(),
                      })
                      setName('')
                      setEntraTenantId('')
                    } catch {
                      // Mutation state already captures the error.
                    }
                  })()
                }}
              >
                {isCreating ? 'Creating tenant...' : 'Add tenant'}
              </Button>
              <p className="text-sm text-muted-foreground">
                After creation you will land on the tenant detail page to review SLA defaults and inventory state.
              </p>
            </div>
          </div>

          <div className="rounded-3xl border border-border/70 bg-background/35 p-4">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">What gets created</p>
            <div className="mt-4 space-y-2">
              <SetupRow label="Tenant record" value="Registered immediately" />
              <SetupRow label="Default SLA policy" value="Critical 7d, High 30d, Medium 90d, Low 180d" />
              <SetupRow label="Tenant source template" value="Microsoft Defender, disabled until credentials are added" />
            </div>
            {createError ? (
              <p className="mt-4 text-sm text-destructive">{createError}</p>
            ) : (
              <p className="mt-4 text-sm text-muted-foreground">
                Use a distinct Entra tenant ID. Duplicate names or tenant IDs are rejected.
              </p>
            )}
          </div>
        </CardContent>
      </Card>

      <DataTableWorkbench
        title="Tenant Directory"
        description="Select a tenant to review identity, SLA, inventory footprint, and source readiness."
        totalCount={totalCount}
      >
        <DataTableToolbar>
          <DataTableToolbarRow>
            <DataTableSummaryStrip items={summaryItems} className="grid-cols-1 sm:grid-cols-3" />
          </DataTableToolbarRow>
        </DataTableToolbar>

        {tenants.length === 0 ? (
          <DataTableEmptyState
            title="No tenants found"
            description="Create the first tenant to start configuring ingestion sources and inventory policy."
          />
        ) : (
          <div className="overflow-hidden rounded-[24px] border border-border/70 bg-background/30">
            <DataTable columns={columns} data={tenants} getRowId={(row) => row.id} className="min-w-[980px]" />
          </div>
        )}

        <PaginationControls
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          totalPages={totalPages}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
        />
      </DataTableWorkbench>
    </section>
  )
}

function SetupRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-3 rounded-xl border border-border/60 bg-card/40 px-3 py-3">
      <span className="text-sm font-medium text-foreground">{label}</span>
      <span className="max-w-[16rem] text-right text-xs text-muted-foreground">{value}</span>
    </div>
  )
}

function GuideItem({
  icon: Icon,
  label,
  text,
}: {
  icon: typeof BadgeCheck
  label: string
  text: string
}) {
  return (
    <div className="flex items-start gap-3 rounded-2xl border border-border/60 bg-background/30 px-3 py-3">
      <span className="mt-0.5 flex size-8 items-center justify-center rounded-xl border border-primary/20 bg-primary/10 text-primary">
        <Icon className="size-4" />
      </span>
      <div>
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <p className="mt-1 text-sm text-foreground">{text}</p>
      </div>
    </div>
  )
}
