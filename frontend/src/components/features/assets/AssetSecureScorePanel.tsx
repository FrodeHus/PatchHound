import { useQuery } from '@tanstack/react-query'
import { Shield, AlertTriangle, Settings2, Gauge } from 'lucide-react'
import { fetchAssetSecureScore } from '@/api/secure-score.functions'
import type { AssetScoreDetail } from '@/api/secure-score.schemas'

type Props = { assetId: string }

function scoreTone(score: number) {
  if (score >= 75) return { label: 'Critical', text: 'text-tone-danger-foreground', bar: 'bg-tone-danger-foreground/80', badge: 'border-tone-danger-border bg-tone-danger text-tone-danger-foreground' }
  if (score >= 50) return { label: 'Elevated', text: 'text-tone-warning-foreground', bar: 'bg-tone-warning-foreground/80', badge: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground' }
  if (score >= 25) return { label: 'Guarded', text: 'text-tone-info-foreground', bar: 'bg-tone-info-foreground/80', badge: 'border-tone-info-border bg-tone-info text-tone-info-foreground' }
  return { label: 'Stable', text: 'text-tone-success-foreground', bar: 'bg-tone-success-foreground/80', badge: 'border-tone-success-border bg-tone-success text-tone-success-foreground' }
}

export function AssetSecureScorePanel({ assetId }: Props) {
  const { data, isFetching, isError } = useQuery({
    queryKey: ['secure-score', 'asset', assetId],
    queryFn: () => fetchAssetSecureScore({ data: { assetId } }),
    staleTime: 60_000,
  })

  if (isError) return null
  if (isFetching && !data) {
    return (
      <section className="rounded-[28px] border border-border/70 bg-card p-4">
        <div className="h-32 animate-pulse rounded-xl bg-muted/60" />
      </section>
    )
  }
  if (!data) return null

  return <ScoreContent score={data} />
}

function ScoreContent({ score }: { score: AssetScoreDetail }) {
  const tone = scoreTone(score.overallScore)

  return (
    <section className="rounded-[28px] border border-border/70 bg-card p-4">
      <div className="flex items-center gap-2">
        <Shield className="size-4 text-primary" />
        <h2 className="text-lg font-semibold">Secure Score</h2>
      </div>
      <p className="mt-1 text-sm text-muted-foreground">
        Composite risk exposure for this asset.
      </p>

      {/* Overall score */}
      <div className="mt-4 flex items-end justify-between gap-3">
        <p className={`text-4xl font-semibold tabular-nums tracking-[-0.04em] ${tone.text}`}>
          {score.overallScore.toFixed(1)}
        </p>
        <span className={`rounded-full border px-2.5 py-1 text-[11px] font-medium ${tone.badge}`}>
          {tone.label}
        </span>
      </div>

      {/* Score bar */}
      <div className="mt-3 h-2 overflow-hidden rounded-full bg-muted/80">
        <div
          className={`h-full rounded-full transition-all ${tone.bar}`}
          style={{ width: `${Math.min(100, score.overallScore)}%` }}
        />
      </div>

      {/* Sub-scores */}
      <div className="mt-4 grid gap-2">
        <SubScore icon={<AlertTriangle className="size-3.5" />} label="Vulnerability" value={score.vulnerabilityScore} />
        <SubScore icon={<Settings2 className="size-3.5" />} label="Configuration" value={score.configurationScore} />
        <SubScore icon={<Gauge className="size-3.5" />} label="Device weight" value={score.deviceValueWeight} suffix="×" />
      </div>

      <p className="mt-3 text-xs text-muted-foreground">
        {score.activeVulnerabilityCount} active vulnerabilities
      </p>

      {/* Factors */}
      {score.factors.length > 0 ? (
        <div className="mt-4 border-t border-border/60 pt-3">
          <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Score factors</p>
          <div className="mt-2 space-y-1.5">
            {score.factors.map((factor) => (
              <div key={factor.name} className="flex items-start justify-between gap-2 text-xs">
                <div className="min-w-0">
                  <p className="font-medium">{factor.name}</p>
                  <p className="text-muted-foreground">{factor.description}</p>
                </div>
                <span className={`shrink-0 tabular-nums font-medium ${factor.impact > 0 ? 'text-tone-danger-foreground' : 'text-tone-success-foreground'}`}>
                  {factor.impact > 0 ? '+' : ''}{factor.impact.toFixed(1)}
                </span>
              </div>
            ))}
          </div>
        </div>
      ) : null}

      <p className="mt-3 text-[10px] text-muted-foreground/60">
        v{score.calculationVersion} · {new Date(score.calculatedAt).toLocaleString()}
      </p>
    </section>
  )
}

function SubScore({ icon, label, value, suffix }: { icon: React.ReactNode; label: string; value: number; suffix?: string }) {
  return (
    <div className="flex items-center justify-between rounded-xl border border-border/70 bg-background px-3 py-2">
      <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
        {icon}
        {label}
      </span>
      <span className="text-sm font-medium tabular-nums">
        {suffix ? `${value.toFixed(1)}${suffix}` : value.toFixed(1)}
      </span>
    </div>
  )
}
