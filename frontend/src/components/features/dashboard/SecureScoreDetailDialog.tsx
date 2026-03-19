import { Shield, AlertTriangle, Settings2, Gauge } from 'lucide-react'
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
import type { SecureScoreSummary, AssetScoreSummary } from '@/api/secure-score.schemas'

type SecureScoreDetailDialogProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  summary: SecureScoreSummary
}

function scoreTone(score: number): string {
  if (score >= 75) return 'text-tone-danger-foreground'
  if (score >= 50) return 'text-tone-warning-foreground'
  if (score >= 25) return 'text-tone-info-foreground'
  return 'text-tone-success-foreground'
}

function scoreBarColor(score: number): string {
  if (score >= 75) return 'bg-tone-danger-foreground/80'
  if (score >= 50) return 'bg-tone-warning-foreground/80'
  if (score >= 25) return 'bg-tone-info-foreground/80'
  return 'bg-tone-success-foreground/80'
}

export function SecureScoreDetailDialog({
  open,
  onOpenChange,
  summary,
}: SecureScoreDetailDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Shield className="size-5 text-primary" />
            Secure Score Breakdown
          </DialogTitle>
          <DialogDescription>
            Tenant score: <span className={`font-semibold ${scoreTone(summary.overallScore)}`}>{summary.overallScore.toFixed(1)}</span>
            {' / '}target: {summary.targetScore.toFixed(0)}
            {' — '}{summary.assetCount} assets, {summary.assetsAboveTarget} above target
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 max-h-[60vh] overflow-y-auto pr-1">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
            Top Risk Assets
          </p>
          {summary.topRiskAssets.length === 0 ? (
            <p className="text-sm text-muted-foreground">No scores calculated yet.</p>
          ) : (
            summary.topRiskAssets.map((asset) => (
              <AssetScoreRow key={asset.assetId} asset={asset} targetScore={summary.targetScore} />
            ))
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

function AssetScoreRow({ asset, targetScore }: { asset: AssetScoreSummary; targetScore: number }) {
  const aboveTarget = asset.overallScore > targetScore

  return (
    <InsetPanel className="p-3 space-y-2">
      <div className="flex items-center justify-between gap-2">
        <Link
          to="/assets/$id"
          params={{ id: asset.assetId }}
          className="text-sm font-medium truncate hover:underline"
        >
          {asset.assetName}
        </Link>
        <span className={`text-lg font-semibold tabular-nums ${scoreTone(asset.overallScore)}`}>
          {asset.overallScore.toFixed(1)}
        </span>
      </div>

      {/* Score bar */}
      <div className="relative h-1.5 overflow-hidden rounded-full bg-muted/80">
        <div
          className={`absolute inset-y-0 left-0 rounded-full transition-all ${scoreBarColor(asset.overallScore)}`}
          style={{ width: `${Math.min(100, asset.overallScore)}%` }}
        />
        <div
          className="absolute inset-y-0 w-0.5 bg-foreground/40"
          style={{ left: `${targetScore}%` }}
        />
      </div>

      {/* Sub-scores */}
      <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
        <span className="flex items-center gap-1">
          <AlertTriangle className="size-3" />
          Vuln: {asset.vulnerabilityScore.toFixed(1)}
        </span>
        <span className="flex items-center gap-1">
          <Settings2 className="size-3" />
          Config: {asset.configurationScore.toFixed(1)}
        </span>
        <span className="flex items-center gap-1">
          <Gauge className="size-3" />
          Weight: ×{asset.deviceValueWeight.toFixed(1)}
        </span>
        {asset.activeVulnerabilityCount > 0 && (
          <Badge variant="outline" className="text-[10px]">
            {asset.activeVulnerabilityCount} vulns
          </Badge>
        )}
        {aboveTarget && (
          <Badge variant="destructive" className="text-[10px]">
            above target
          </Badge>
        )}
      </div>
    </InsetPanel>
  )
}
