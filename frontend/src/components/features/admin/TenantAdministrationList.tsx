import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { ArrowRight, BadgeCheck, Building2, KeyRound, ShieldCheck } from 'lucide-react'
import type { TenantListItem } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

type TenantAdministrationListProps = {
  tenants: TenantListItem[]
  totalCount: number
  isCreating: boolean
  createError: string | null
  onCreate: (payload: { name: string; entraTenantId: string }) => Promise<unknown>
}

export function TenantAdministrationList({
  tenants,
  totalCount,
  isCreating,
  createError,
  onCreate,
}: TenantAdministrationListProps) {
  const [name, setName] = useState('')
  const [entraTenantId, setEntraTenantId] = useState('')

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

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-1">
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

      <Card className="rounded-[28px] border-border/70 bg-card/82">
        <CardHeader>
          <div className="flex items-end justify-between gap-3">
            <div>
              <CardTitle>Configured Tenants</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">Select a tenant to review source credentials and sync cadence.</p>
            </div>
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{totalCount} total</p>
          </div>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Tenant</TableHead>
                <TableHead>Entra Tenant ID</TableHead>
                <TableHead>Configured Sources</TableHead>
                <TableHead className="text-right">Open</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {tenants.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={4} className="py-6 text-center text-muted-foreground">
                    No tenants found.
                  </TableCell>
                </TableRow>
              ) : (
                tenants.map((tenant) => (
                  <TableRow key={tenant.id}>
                    <TableCell className="font-medium">{tenant.name}</TableCell>
                    <TableCell>
                      <code className="rounded bg-muted px-2 py-1 text-xs">{tenant.entraTenantId}</code>
                    </TableCell>
                    <TableCell>{tenant.configuredIngestionSourceCount}</TableCell>
                    <TableCell className="text-right">
                      <Link
                        to="/admin/tenants/$id"
                        params={{ id: tenant.id }}
                        className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
                      >
                        View detail
                        <ArrowRight className="size-4" />
                      </Link>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
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
