import { Link } from '@tanstack/react-router'
import { ArrowLeft, ShieldAlert } from 'lucide-react'
import type { OwnerAssetSummary } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

type Props = {
  items: OwnerAssetSummary[]
  isLoading: boolean
}

function formatRiskScore(value: number | null) {
  if (value === null) return '0'
  return Math.round(value).toString()
}

export function AssetOwnerAttentionView({ items, isLoading }: Props) {
  return (
    <section className="space-y-6 pb-4">
      <Card className="overflow-hidden rounded-[2rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--destructive)_10%,var(--background)),var(--card)_58%,var(--background))] shadow-[0_28px_70px_-48px_rgba(0,0,0,0.55)]">
        <CardContent className="p-6 sm:p-8">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
            <div className="space-y-4">
              <Badge variant="outline" className="rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-destructive">
                Attention queue
              </Badge>
              <div>
                <h1 className="text-3xl font-semibold tracking-[-0.06em] sm:text-4xl">
                  Your assets that need attention now
                </h1>
                <p className="mt-3 max-w-3xl text-base leading-7 text-muted-foreground">
                  This is the owner-scoped list behind the dashboard metric. It only includes assets you own that currently carry elevated risk pressure and should be reviewed rather than left in routine monitoring.
                </p>
              </div>
            </div>
            <div className="flex items-center gap-3">
              <div className="rounded-[1.2rem] border border-destructive/20 bg-background/70 px-4 py-4 text-right">
                <div className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Need attention</div>
                <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{items.length}</div>
              </div>
              <Button variant="outline" render={<Link to="/dashboard/my-assets" />}>
                <ArrowLeft className="mr-2 size-4" />
                Back to dashboard
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card className="rounded-[1.6rem] border-border/70">
        <CardHeader>
          <CardTitle>Assets needing attention</CardTitle>
          <CardDescription>
            Ordered by current risk score, with the strongest driver shown in plain language.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {isLoading ? (
            <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
              Loading your owned assets…
            </div>
          ) : items.length === 0 ? (
            <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
              No owned assets currently need attention.
            </div>
          ) : (
            items.map((item) => (
              <div key={item.assetId} className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4">
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                        {item.criticality}
                      </Badge>
                      <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                        {item.deviceGroupName || 'Ungrouped'}
                      </Badge>
                      {item.riskBand ? (
                        <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                          {item.riskBand}
                        </Badge>
                      ) : null}
                    </div>
                    <div className="mt-2 text-lg font-medium tracking-tight">{item.assetName}</div>
                    <div className="mt-1 text-sm text-muted-foreground">
                      {item.topDriverSummary || 'This asset has unresolved exposure that should be reviewed.'}
                    </div>
                    <div className="mt-2 text-sm text-muted-foreground">
                      {item.openEpisodeCount} open exposure items
                      {item.topDriverTitle ? ` • Top driver: ${item.topDriverTitle}` : ''}
                    </div>
                  </div>

                  <div className="min-w-32 text-right">
                    <div className="flex items-center justify-end gap-1 text-xs uppercase tracking-[0.18em] text-muted-foreground">
                      <ShieldAlert className="size-3.5 text-destructive" />
                      Risk score
                    </div>
                    <div className="mt-2 text-3xl font-semibold tracking-[-0.05em] text-destructive">
                      {formatRiskScore(item.currentRiskScore)}
                    </div>
                  </div>
                </div>

                <div className="mt-3">
                  <Button size="sm" variant="outline" render={<Link to="/assets/$id" params={{ id: item.assetId }} />}>
                    Review asset
                  </Button>
                </div>
              </div>
            ))
          )}
        </CardContent>
      </Card>
    </section>
  )
}
