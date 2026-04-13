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
import type { RiskAssetEpisodeDriver, RiskAssetScoreSummary, RiskScoreSummary } from '@/api/risk-score.schemas'

type RiskScoreDetailDialogProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  summary: RiskScoreSummary
}

export function RiskScoreDetailDialog({
  open,
  onOpenChange,
  summary,
}: RiskScoreDetailDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Flame className="size-5 text-destructive" />
            Risk Score Drivers
          </DialogTitle>
          <DialogDescription>
            Current exposure is driven by {summary.assetCount} scored assets, with{' '}
            {summary.criticalAssetCount} critical and {summary.highAssetCount} high-risk assets
            currently above the rest of the fleet.
          </DialogDescription>
        </DialogHeader>

        <div className="max-h-[60vh] space-y-3 overflow-y-auto pr-1">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
            Top Risk Assets
          </p>
          {summary.topRiskAssets.length === 0 ? (
            <p className="text-sm text-muted-foreground">No active episode-backed risk scores yet.</p>
          ) : (
            summary.topRiskAssets.map((asset) => (
              <RiskAssetRow key={asset.assetId} asset={asset} />
            ))
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

function RiskAssetRow({ asset }: { asset: RiskAssetScoreSummary }) {
  const band = riskBand(asset.overallScore)

  return (
    <InsetPanel className="space-y-3 p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <Link
            to="/devices/$id"
            params={{ id: asset.assetId }}
            className="truncate text-sm font-medium hover:underline"
          >
            {asset.assetName}
          </Link>
          <p className="mt-1 text-xs text-muted-foreground">
            Peak episode risk {asset.maxEpisodeRiskScore.toFixed(0)} across {asset.openEpisodeCount}{' '}
            open episodes.
          </p>
        </div>
        <Badge className={band.badgeClass}>{asset.overallScore.toFixed(0)}</Badge>
      </div>

      <div className="grid gap-2 sm:grid-cols-4">
        <RiskMetric
          icon={Siren}
          label="Critical"
          value={asset.criticalCount}
          tone="text-destructive"
        />
        <RiskMetric
          icon={ShieldAlert}
          label="High"
          value={asset.highCount}
          tone="text-tone-warning-foreground"
        />
        <RiskMetric
          icon={TriangleAlert}
          label="Medium"
          value={asset.mediumCount}
          tone="text-chart-2"
        />
        <RiskMetric
          icon={Flame}
          label="Low"
          value={asset.lowCount}
          tone="text-muted-foreground"
        />
      </div>

      {asset.episodeDrivers.length > 0 ? (
        <div className="space-y-2">
          <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
            Top Episode Drivers
          </p>
          <div className="space-y-2">
            {asset.episodeDrivers.map((driver) => (
              <EpisodeDriverRow key={driver.tenantVulnerabilityId} driver={driver} />
            ))}
          </div>
        </div>
      ) : null}
    </InsetPanel>
  )
}

function EpisodeDriverRow({ driver }: { driver: RiskAssetEpisodeDriver }) {
  const badgeClass = riskBand(driver.episodeRiskScore).badgeClass

  return (
    <div className="rounded-xl border border-border/70 bg-background/35 px-3 py-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <Link
            to="/vulnerabilities/$id"
            params={{ id: driver.tenantVulnerabilityId }}
            className="truncate text-sm font-medium hover:underline"
          >
            {driver.externalId}
          </Link>
          <p className="mt-1 truncate text-xs text-muted-foreground">{driver.title}</p>
        </div>
        <Badge className={badgeClass}>{driver.episodeRiskScore.toFixed(0)}</Badge>
      </div>
      <div className="mt-3 grid gap-2 sm:grid-cols-3">
        <DriverFactor label="Threat" value={driver.threatScore} />
        <DriverFactor label="Context" value={driver.contextScore} />
        <DriverFactor label="Operational" value={driver.operationalScore} />
      </div>
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

function DriverFactor({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-lg border border-border/60 bg-background/30 px-2.5 py-2">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-base font-semibold tabular-nums">{value.toFixed(0)}</p>
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
