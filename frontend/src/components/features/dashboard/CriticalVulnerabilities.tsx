import { useNavigate } from '@tanstack/react-router'
import type { DashboardSummary, TopVulnerability } from '@/api/dashboard.schemas'
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

type CriticalVulnerabilitiesProps = {
  items: TopVulnerability[]
  summary: DashboardSummary
}

export function CriticalVulnerabilities({ items, summary }: CriticalVulnerabilitiesProps) {
  const navigate = useNavigate()

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="p-5 pb-2">
        <div className="flex items-center justify-between gap-3">
          <div>
            <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Priority queue</p>
            <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Top critical vulnerabilities</CardTitle>
          </div>
          <Badge className="rounded-full border border-destructive/20 bg-destructive/10 text-destructive hover:bg-destructive/10">
            Critical
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="overflow-x-auto p-5 pt-1">
        <div className="mb-4 grid gap-3 sm:grid-cols-2">
          <div className="rounded-2xl border border-border/70 bg-background/35 p-4">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Recurring vulnerabilities</p>
            <p className="mt-2 text-2xl font-semibold">{summary.recurringVulnerabilityCount}</p>
            <p className="mt-1 text-xs text-muted-foreground">{summary.recurrenceRatePercent}% of tracked vulnerability-asset histories have recurred.</p>
          </div>
          <div className="rounded-2xl border border-border/70 bg-background/35 p-4">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Top recurring asset</p>
            <p className="mt-2 text-sm font-semibold">{summary.topRecurringAssets[0]?.name ?? 'No recurring assets yet'}</p>
            <p className="mt-1 text-xs text-muted-foreground">
              {summary.topRecurringAssets[0]
                ? `${summary.topRecurringAssets[0].recurringVulnerabilityCount} recurring vulnerabilities`
                : 'Waiting for recurrence history.'}
            </p>
          </div>
        </div>
        <Table className="min-w-[520px]">
          <TableHeader>
            <TableRow className="border-border/60 hover:bg-transparent">
              <TableHead>External ID</TableHead>
              <TableHead>Title</TableHead>
              <TableHead>Severity</TableHead>
              <TableHead className="text-right">Assets</TableHead>
              <TableHead className="text-right">Age</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.length === 0 ? (
              <TableRow className="border-border/60 hover:bg-transparent">
                <TableCell colSpan={5} className="py-8 text-center text-muted-foreground">
                  No critical vulnerabilities found.
                </TableCell>
              </TableRow>
            ) : (
              items.map((item) => (
                <TableRow
                  key={item.id}
                  tabIndex={0}
                  className="cursor-pointer border-border/50 transition-colors hover:bg-accent/20 focus-visible:bg-accent/20 focus-visible:outline-none"
                  onClick={() => {
                    void navigate({ to: '/vulnerabilities/$id', params: { id: item.id } })
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault()
                      void navigate({ to: '/vulnerabilities/$id', params: { id: item.id } })
                    }
                  }}
                >
                  <TableCell className="font-medium text-foreground transition-colors group-hover:text-primary">
                    {item.externalId}
                  </TableCell>
                  <TableCell className="max-w-[18rem] truncate text-muted-foreground">
                    {item.title}
                  </TableCell>
                  <TableCell>
                    <Badge className="rounded-full border border-destructive/20 bg-destructive/10 text-destructive hover:bg-destructive/10">
                      {item.severity}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">{item.affectedAssetCount}</TableCell>
                  <TableCell className="text-right text-muted-foreground">{item.daysSincePublished}d</TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
        {summary.topRecurringVulnerabilities.length > 0 ? (
          <div className="mt-5 rounded-2xl border border-border/70 bg-background/35 p-4">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Top recurring CVEs</p>
            <div className="mt-3 space-y-2">
              {summary.topRecurringVulnerabilities.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className="flex w-full items-center justify-between gap-3 rounded-xl border border-border/60 bg-card/40 px-3 py-2 text-left hover:bg-accent/20"
                  onClick={() => {
                    void navigate({ to: '/vulnerabilities/$id', params: { id: item.id } })
                  }}
                >
                  <div>
                    <p className="text-sm font-medium">{item.externalId}</p>
                    <p className="text-xs text-muted-foreground">{item.title}</p>
                  </div>
                  <Badge variant="outline" className="rounded-full border-amber-300/70 bg-amber-50 text-amber-900">
                    {item.reappearanceCount}x
                  </Badge>
                </button>
              ))}
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}
