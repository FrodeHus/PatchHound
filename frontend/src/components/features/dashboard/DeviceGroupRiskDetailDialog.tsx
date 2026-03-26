import { useQuery } from '@tanstack/react-query'
import { CircleQuestionMark, Flame, ShieldAlert, Siren, TriangleAlert } from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Badge } from '@/components/ui/badge'
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from '@/components/ui/popover'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Skeleton } from '@/components/ui/skeleton'
import { fetchDeviceGroupRiskDetail } from '@/api/risk-score.functions'
import type { DeviceGroupRiskDetail, RiskAssetScoreSummary } from '@/api/risk-score.schemas'
import { Link } from '@tanstack/react-router'

type DeviceGroupRiskDetailDialogProps = {
  deviceGroupName: string | null
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function DeviceGroupRiskDetailDialog({
  deviceGroupName,
  open,
  onOpenChange,
}: DeviceGroupRiskDetailDialogProps) {
  const { data, isFetching } = useQuery({
    queryKey: ['risk-score', 'device-group-detail', deviceGroupName],
    queryFn: () => fetchDeviceGroupRiskDetail({ data: { deviceGroupName: deviceGroupName ?? '' } }),
    enabled: open && Boolean(deviceGroupName),
    staleTime: 30_000,
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Flame className="size-5 text-destructive" />
            {deviceGroupName ?? 'Device Group'} Risk
          </DialogTitle>
          <DialogDescription>
            Current device-group pressure with the highest-risk assets driving exposure.
          </DialogDescription>
        </DialogHeader>

        {isFetching && !data ? (
          <div className="space-y-3">
            <Skeleton className="h-20 w-full rounded-2xl" />
            <Skeleton className="h-28 w-full rounded-2xl" />
          </div>
        ) : !data ? (
          <p className="text-sm text-muted-foreground">No risk detail is available for this device group.</p>
        ) : (
          <div className="space-y-4">
            <InsetPanel className="grid gap-3 rounded-[24px] p-4 sm:grid-cols-4">
              <Metric
                label="Risk score"
                value={data.overallScore.toFixed(0)}
                info={data.explanation ? <RollupRiskExplanationPopover title="Device-group risk breakdown" explanation={data.explanation} /> : null}
              />
              <Metric label="Assets" value={String(data.assetCount)} />
              <Metric label="Open episodes" value={String(data.openEpisodeCount)} />
              <Metric label="Critical episodes" value={String(data.criticalEpisodeCount)} />
            </InsetPanel>

            <div className="space-y-3">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Top Risk Assets
              </p>
              {data.topRiskAssets.length === 0 ? (
                <p className="text-sm text-muted-foreground">No scored assets currently drive this group.</p>
              ) : (
                data.topRiskAssets.map((asset) => <GroupAssetRow key={asset.assetId} asset={asset} />)
              )}
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

function GroupAssetRow({ asset }: { asset: RiskAssetScoreSummary }) {
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

function Metric({ label, value, info }: { label: string; value: string; info?: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/35 px-3 py-3">
      <div className="flex items-center gap-1.5">
        <p className="text-xs uppercase tracking-[0.15em] text-muted-foreground">{label}</p>
        {info}
      </div>
      <p className="mt-2 text-2xl font-semibold tracking-[-0.04em]">{value}</p>
    </div>
  )
}

function RollupRiskExplanationPopover({
  title,
  explanation,
}: {
  title: string
  explanation: NonNullable<DeviceGroupRiskDetail['explanation']>
}) {
  return (
    <Popover>
      <PopoverTrigger className="inline-flex items-center rounded-full text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
        <CircleQuestionMark className="size-4" />
      </PopoverTrigger>
      <PopoverContent side="left" align="end" sideOffset={10} className="w-[28rem] gap-3 rounded-2xl p-4">
        <PopoverHeader>
          <PopoverTitle>{title}</PopoverTitle>
          <PopoverDescription>
            This rollup score is calculated from the highest asset risk, the top-three average, and severity-volume bonuses in this scope.
          </PopoverDescription>
        </PopoverHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <Metric label="Score" value={explanation.score.toFixed(0)} />
          <Metric label="Formula version" value={explanation.calculationVersion} />
          <Metric label="Assets" value={String(explanation.assetCount)} />
          <Metric label="Open episodes" value={String(explanation.openEpisodeCount)} />
          <Metric label="Max asset risk" value={explanation.maxAssetRiskScore.toFixed(0)} />
          <Metric label="Top 3 average" value={explanation.topThreeAverage.toFixed(2)} />
        </div>
        <div className="rounded-2xl border border-border/70 bg-background/60 p-3">
          <p className="text-xs font-medium text-muted-foreground">Formula</p>
          <p className="mt-2 text-sm leading-relaxed text-foreground/90">
            Weighted top-risk asset + weighted top-three average + severity bonuses.
          </p>
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            <Metric label="Max asset contribution" value={explanation.maxAssetContribution.toFixed(2)} />
            <Metric label="Top 3 contribution" value={explanation.topThreeContribution.toFixed(2)} />
            <Metric label={`Critical (${explanation.criticalEpisodeCount})`} value={explanation.criticalContribution.toFixed(2)} />
            <Metric label={`High (${explanation.highEpisodeCount})`} value={explanation.highContribution.toFixed(2)} />
            <Metric label={`Medium (${explanation.mediumEpisodeCount})`} value={explanation.mediumContribution.toFixed(2)} />
            <Metric label={`Low (${explanation.lowEpisodeCount})`} value={explanation.lowContribution.toFixed(2)} />
          </div>
        </div>
        <div className="space-y-2">
          <p className="text-xs font-medium text-muted-foreground">Persisted factors</p>
          {explanation.factors.map((factor) => (
            <div key={factor.name} className="rounded-xl border border-border/70 bg-background/60 px-3 py-2.5">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm font-medium text-foreground">{factor.name}</p>
                  <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{factor.description}</p>
                </div>
                <span className="text-sm font-semibold tabular-nums text-foreground">{factor.impact.toFixed(2)}</span>
              </div>
            </div>
          ))}
        </div>
      </PopoverContent>
    </Popover>
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
    return {
      badgeClass: 'border-destructive/25 bg-destructive/10 text-destructive',
    }
  }
  if (score >= 750) {
    return {
      badgeClass:
        'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
    }
  }
  if (score >= 500) {
    return {
      badgeClass: 'border-chart-2/25 bg-chart-2/10 text-chart-2',
    }
  }
  return {
    badgeClass: 'border-border/70 bg-background/40 text-muted-foreground',
  }
}
