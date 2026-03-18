import { useState } from 'react'
import type { AffectedAsset } from '@/api/vulnerabilities.schemas'
import { toneBadge, toneDot, toneSurface } from '@/lib/tone-classes'

type AffectedAssetsTabProps = {
  assets: AffectedAsset[]
}

export function AffectedAssetsTab({ assets }: AffectedAssetsTabProps) {
  const [expandedAssetId, setExpandedAssetId] = useState<string | null>(assets[0]?.assetId ?? null)

  if (assets.length === 0) {
    return (
      <section className="rounded-lg border border-border bg-card p-4">
        <div className="space-y-1">
          <h3 className="text-lg font-semibold">Affected Assets</h3>
          <p className="text-sm text-muted-foreground">No affected assets are currently linked to this vulnerability.</p>
        </div>
      </section>
    )
  }

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h3 className="text-lg font-semibold">Affected Assets</h3>
          <p className="text-sm text-muted-foreground">
            Review where the vulnerability is present, how severe it is in context, and whether it has recurred.
          </p>
        </div>
        <p className="text-xs text-muted-foreground">{assets.length} linked assets</p>
      </div>

      <div className="space-y-3">
        {assets.map((asset) => {
          const isExpanded = expandedAssetId === asset.assetId

          return (
            <article key={asset.assetId} className="rounded-xl border border-border/70 bg-background">
              <button
                type="button"
                className="w-full px-4 py-4 text-left"
                onClick={() => {
                  setExpandedAssetId((current) => (current === asset.assetId ? null : asset.assetId))
                }}
              >
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div className="min-w-0 flex-1 space-y-3">
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="text-sm font-semibold text-foreground">{asset.assetName}</p>
                      <Badge tone="slate" label={asset.assetType} />
                      <Badge tone={asset.status === 'Open' ? 'blue' : 'muted'} label={asset.status} />
                      {asset.episodeCount > 1 ? (
                        <Badge tone="amber" label={`Recurred ${asset.episodeCount}x`} />
                      ) : null}
                    </div>

                    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                      <Metric label="Effective severity" value={formatSeverity(asset.effectiveSeverity, asset.effectiveScore)} />
                      <Metric label="Security profile" value={asset.securityProfileName ?? 'No profile'} />
                      <Metric label="Detected" value={new Date(asset.detectedDate).toLocaleString()} />
                      <Metric label="Resolved" value={asset.resolvedDate ? new Date(asset.resolvedDate).toLocaleString() : 'Still open'} />
                    </div>

                    {asset.assessmentReasonSummary ? (
                      <div className={`rounded-lg border px-3 py-2 text-sm ${toneSurface('info')} text-tone-info-foreground`}>
                        {asset.assessmentReasonSummary}
                      </div>
                    ) : null}

                    {asset.possibleCorrelatedSoftware.length > 0 ? (
                      <div className="space-y-1">
                        <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Likely overlapping software</p>
                        <div className="flex flex-wrap gap-2">
                          {asset.possibleCorrelatedSoftware.map((software) => (
                            <span
                              key={software}
                              className={`rounded-full border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.12em] ${toneBadge('warning')}`}
                            >
                              {software}
                            </span>
                          ))}
                        </div>
                      </div>
                    ) : null}
                  </div>

                  <div className="flex flex-col items-end gap-2">
                    <div className="flex flex-wrap justify-end gap-2">
                      {asset.episodes.map((episode) => (
                        <span
                          key={episode.episodeNumber}
                          className="rounded-full border border-border/70 bg-card px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground"
                        >
                          #{episode.episodeNumber} {episode.status}
                        </span>
                      ))}
                    </div>
                    <span className="text-xs text-muted-foreground">
                      {isExpanded ? 'Hide recurrence timeline' : 'Show recurrence timeline'}
                    </span>
                  </div>
                </div>
              </button>

              {isExpanded ? (
                <div className="border-t border-border/70 px-4 py-4">
                  <div className="mb-3">
                    <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Episode timeline</p>
                    <p className="mt-1 text-sm text-muted-foreground">
                      Each episode represents a continuous period where this vulnerability remained present on the asset.
                    </p>
                  </div>
                  <div className="space-y-3">
                    {[...asset.episodes]
                      .sort((left, right) => new Date(right.firstSeenAt).getTime() - new Date(left.firstSeenAt).getTime())
                      .map((episode, index) => (
                        <div key={episode.episodeNumber} className="flex gap-3">
                          <div className="flex w-5 flex-col items-center">
                            <span
                              className={`mt-1 h-2.5 w-2.5 rounded-full ${
                                episode.episodeNumber > 1 ? toneDot('warning') : toneDot('info')
                              }`}
                            />
                            {index < asset.episodes.length - 1 ? <span className="mt-1 h-full w-px bg-border/80" /> : null}
                          </div>
                          <div className="flex-1 rounded-xl border border-border/70 bg-card px-3 py-3">
                            <div className="flex flex-wrap items-center justify-between gap-2">
                              <p className="text-sm font-medium">
                                Episode #{episode.episodeNumber} {episode.episodeNumber > 1 ? 'reappeared' : 'detected'}
                              </p>
                              <span className="text-xs text-muted-foreground">{new Date(episode.firstSeenAt).toLocaleString()}</span>
                            </div>
                            <p className="mt-2 text-sm text-muted-foreground">
                              Last seen {new Date(episode.lastSeenAt).toLocaleString()}
                              {episode.resolvedAt ? `, resolved ${new Date(episode.resolvedAt).toLocaleString()}.` : ', still open.'}
                            </p>
                          </div>
                        </div>
                      ))}
                  </div>
                </div>
              ) : null}
            </article>
          )
        })}
      </div>
    </section>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="space-y-1">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="text-sm text-foreground">{value}</p>
    </div>
  )
}

function Badge({
  label,
  tone,
}: {
  label: string
  tone: 'slate' | 'blue' | 'amber' | 'muted'
}) {
  const className =
    tone === 'amber'
      ? toneBadge('warning')
      : tone === 'blue'
        ? toneBadge('info')
        : tone === 'muted'
          ? 'border-border/70 bg-card text-muted-foreground'
          : toneBadge('neutral')

  return (
    <span className={`rounded-full border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.12em] ${className}`}>
      {label}
    </span>
  )
}

function formatSeverity(severity: string, score: number | null) {
  return score ? `${severity} (${score.toFixed(1)})` : severity
}
