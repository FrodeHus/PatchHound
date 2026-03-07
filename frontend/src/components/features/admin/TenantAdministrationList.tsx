import { Link } from '@tanstack/react-router'
import { ArrowRight, Building2, KeyRound } from 'lucide-react'
import type { TenantListItem } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
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
}

export function TenantAdministrationList({ tenants, totalCount }: TenantAdministrationListProps) {
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
