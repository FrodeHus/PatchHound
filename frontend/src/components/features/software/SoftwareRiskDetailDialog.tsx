import { useQuery } from '@tanstack/react-query'
import { Flame, ShieldAlert, Siren, TriangleAlert } from 'lucide-react'
import { Link } from '@tanstack/react-router'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Badge } from '@/components/ui/badge'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Skeleton } from '@/components/ui/skeleton'
import { fetchSoftwareRiskDetail } from '@/api/risk-score.functions'
import type { RiskAssetScoreSummary } from '@/api/risk-score.schemas'

type SoftwareRiskDetailDialogProps = {
  tenantSoftwareId: string | null
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function SoftwareRiskDetailDialog({
  tenantSoftwareId,
  open,
  onOpenChange,
}: SoftwareRiskDetailDialogProps) {
  const { data, isFetching } = useQuery({
    queryKey: ['risk-score', 'software-detail', tenantSoftwareId],
    queryFn: () => fetchSoftwareRiskDetail({ data: { tenantSoftwareId: tenantSoftwareId ?? '' } }),
    enabled: open && Boolean(tenantSoftwareId),
    staleTime: 30_000,
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Flame className="size-5 text-destructive" />
            {data?.softwareName ?? 'Software'} Risk
          </DialogTitle>
          <DialogDescription>
            Aggregate software risk with the top installed assets currently driving exposure.
          </DialogDescription>
        </DialogHeader>

        {isFetching && !data ? (
          <div className="space-y-3">
            <Skeleton className="h-20 w-full rounded-2xl" />
            <Skeleton className="h-28 w-full rounded-2xl" />
          </div>
        ) : !data ? (
          <p className="text-sm text-muted-foreground">No risk detail is available for this software.</p>
        ) : (
          <div className="space-y-4">
            <InsetPanel className="grid gap-3 rounded-[24px] p-4 sm:grid-cols-4">
              <Metric label="Risk score" value={data.overallScore.toFixed(0)} />
              <Metric label="Devices" value={String(data.affectedDeviceCount)} />
              <Metric label="Open episodes" value={String(data.openEpisodeCount)} />
              <Metric label="Critical episodes" value={String(data.criticalEpisodeCount)} />
            </InsetPanel>

            <div className="space-y-3">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Top Risk Assets
              </p>
              {data.topRiskAssets.length === 0 ? (
                <p className="text-sm text-muted-foreground">No scored assets currently drive this software.</p>
              ) : (
                data.topRiskAssets.map((asset) => <SoftwareRiskAssetRow key={asset.assetId} asset={asset} />)
              )}
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

function SoftwareRiskAssetRow({ asset }: { asset: RiskAssetScoreSummary }) {
  return (
    <InsetPanel className="space-y-3 p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <Link
            to="/assets/$id"
            params={{ id: asset.assetId }}
            className="truncate text-sm font-medium hover:underline"
          >
            {asset.assetName}
          </Link>
          <p className="mt-1 text-xs text-muted-foreground">
            Peak episode risk {asset.maxEpisodeRiskScore.toFixed(0)} across {asset.openEpisodeCount} open episodes.
          </p>
        </div>
        <Badge className={riskBand(asset.overallScore).badgeClass}>{asset.overallScore.toFixed(0)}</Badge>
      </div>

      <div className="grid gap-2 sm:grid-cols-4">
        <RiskMetric icon={Siren} label="Critical" value={asset.criticalCount} tone="text-destructive" />
        <RiskMetric icon={ShieldAlert} label="High" value={asset.highCount} tone="text-tone-warning-foreground" />
        <RiskMetric icon={TriangleAlert} label="Medium" value={asset.mediumCount} tone="text-chart-2" />
        <RiskMetric icon={Flame} label="Low" value={asset.lowCount} tone="text-muted-foreground" />
      </div>
    </InsetPanel>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/35 px-3 py-3">
      <p className="text-xs uppercase tracking-[0.15em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-2xl font-semibold tracking-[-0.04em]">{value}</p>
    </div>
  )
}

function RiskMetric({
  icon: Icon,
  label,
  value,
  tone,
}: {
  icon: typeof Flame
  label: string
  value: number
  tone: string
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/35 px-3 py-2">
      <div className={`flex items-center gap-1.5 text-xs ${tone}`}>
        <Icon className="size-3.5" />
        {label}
      </div>
      <p className="mt-1 text-lg font-semibold tabular-nums">{value}</p>
    </div>
  )
}

function riskBand(score: number) {
  if (score >= 900) {
    return { badgeClass: 'border-destructive/25 bg-destructive/10 text-destructive' }
  }
  if (score >= 750) {
    return { badgeClass: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground' }
  }
  if (score >= 500) {
    return { badgeClass: 'border-chart-2/25 bg-chart-2/10 text-chart-2' }
  }
  return { badgeClass: 'border-border/70 bg-background/40 text-muted-foreground' }
}
