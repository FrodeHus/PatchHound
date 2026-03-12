import { createFileRoute, Link } from '@tanstack/react-router'
import { fetchDashboardRiskChanges } from '@/api/dashboard.functions'
import type { DashboardRiskChangeBrief } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'

export const Route = createFileRoute('/_authed/vulnerabilities/changes')({
  loader: () => fetchDashboardRiskChanges(),
  component: VulnerabilityChangesPage,
})

function VulnerabilityChangesPage() {
  const brief = Route.useLoaderData()

  return (
    <section className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Change log</p>
          <h1 className="mt-2 text-2xl font-semibold tracking-tight">High and critical risk changes</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Appeared and resolved tenant vulnerabilities from the last 24 hours.
          </p>
        </div>
        <Badge variant="outline" className="rounded-full border-border/70 bg-muted text-foreground">
          Last 24 hours
        </Badge>
      </header>

      <div className="grid gap-4 xl:grid-cols-2">
        <ChangeListCard title="New" items={brief.appeared} emptyText="No new high or critical vulnerabilities in the last 24 hours." />
        <ChangeListCard title="Resolved" items={brief.resolved} emptyText="No high or critical vulnerabilities resolved in the last 24 hours." />
      </div>
    </section>
  )
}

type ChangeListCardProps = {
  title: string
  items: DashboardRiskChangeBrief['appeared']
  emptyText: string
}

function ChangeListCard({ title, items, emptyText }: ChangeListCardProps) {
  return (
    <Card className="rounded-2xl border-border/70 bg-card/92">
      <CardHeader className="p-5 pb-2">
        <CardTitle className="text-lg font-semibold tracking-tight">{title}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3 p-5 pt-1">
        {items.length === 0 ? (
          <InsetPanel className="p-4">
            <p className="text-sm text-muted-foreground">{emptyText}</p>
          </InsetPanel>
        ) : (
          items.map((item) => (
            <InsetPanel key={item.tenantVulnerabilityId} className="p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <Link
                      to="/vulnerabilities/$id"
                      params={{ id: item.tenantVulnerabilityId }}
                      className="text-sm font-medium hover:text-primary hover:underline"
                    >
                      {item.externalId}
                    </Link>
                    <Badge
                      variant="outline"
                      className={
                        item.severity === 'Critical'
                          ? 'rounded-full border-destructive/30 bg-destructive/10 text-destructive'
                          : 'rounded-full border-amber-300/30 bg-amber-500/10 text-amber-200'
                      }
                    >
                      {item.severity}
                    </Badge>
                  </div>
                  <p className="mt-2 text-sm text-foreground">{item.title}</p>
                  <p className="mt-2 text-xs text-muted-foreground">
                    {item.affectedAssetCount} {item.affectedAssetCount === 1 ? 'asset' : 'assets'} • {new Date(item.changedAt).toLocaleString()}
                  </p>
                </div>
              </div>
            </InsetPanel>
          ))
        )}
      </CardContent>
    </Card>
  )
}
