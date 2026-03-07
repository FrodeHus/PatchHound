import { Fragment, useState } from 'react'
import type { AffectedAsset } from '@/api/vulnerabilities.schemas'

type AffectedAssetsTabProps = {
  assets: AffectedAsset[]
}

export function AffectedAssetsTab({ assets }: AffectedAssetsTabProps) {
  const [expandedAssetId, setExpandedAssetId] = useState<string | null>(assets[0]?.assetId ?? null)

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="mb-3 text-lg font-semibold">Affected Assets</h3>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[720px] border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">Asset</th>
              <th className="py-2 pr-2">Type</th>
              <th className="py-2 pr-2">Status</th>
              <th className="py-2 pr-2">Episodes</th>
              <th className="py-2 pr-2">Detected</th>
              <th className="py-2 pr-2">Resolved</th>
            </tr>
          </thead>
          <tbody>
            {assets.map((asset) => {
              const isExpanded = expandedAssetId === asset.assetId

              return (
                <Fragment key={asset.assetId}>
                  <tr
                    className="cursor-pointer border-b border-border/60 hover:bg-muted/20"
                    onClick={() => {
                      setExpandedAssetId((current) => current === asset.assetId ? null : asset.assetId)
                    }}
                  >
                    <td className="py-2 pr-2">
                      <div className="space-y-1">
                        <div className="flex items-center gap-2">
                          <span className="font-medium">{asset.assetName}</span>
                          <span className="rounded-full border border-border/70 bg-background px-2 py-0.5 text-[11px] text-muted-foreground">
                            {isExpanded ? 'Hide timeline' : 'Show timeline'}
                          </span>
                        </div>
                        {asset.possibleCorrelatedSoftware.length > 0 ? (
                          <p className="text-xs text-amber-700">
                            Likely overlap: {asset.possibleCorrelatedSoftware.join(', ')}
                          </p>
                        ) : null}
                      </div>
                    </td>
                    <td className="py-2 pr-2">{asset.assetType}</td>
                    <td className="py-2 pr-2">{asset.status}</td>
                    <td className="py-2 pr-2">
                      <div className="flex flex-wrap gap-1">
                        {asset.episodes.map((episode) => (
                          <span key={episode.episodeNumber} className="rounded-full border border-border/70 bg-background px-2 py-0.5 text-xs">
                            #{episode.episodeNumber} {episode.status === 'Open' ? 'Open' : 'Closed'}
                          </span>
                        ))}
                      </div>
                    </td>
                    <td className="py-2 pr-2">{new Date(asset.detectedDate).toLocaleString()}</td>
                    <td className="py-2 pr-2">{asset.resolvedDate ? new Date(asset.resolvedDate).toLocaleString() : '-'}</td>
                  </tr>
                  {isExpanded ? (
                    <tr className="border-b border-border/60 bg-muted/10">
                      <td colSpan={6} className="px-0 py-0">
                        <div className="space-y-3 p-4">
                          <div>
                            <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Episode timeline</p>
                            <p className="mt-1 text-sm text-muted-foreground">
                              Each episode represents a continuous period where the vulnerability remained present on this asset.
                            </p>
                          </div>
                          <div className="space-y-3">
                            {[...asset.episodes]
                              .sort((left, right) => new Date(right.firstSeenAt).getTime() - new Date(left.firstSeenAt).getTime())
                              .map((episode, index) => (
                                <div key={episode.episodeNumber} className="flex gap-3">
                                  <div className="flex w-5 flex-col items-center">
                                    <span className={`mt-1 h-2.5 w-2.5 rounded-full ${episode.episodeNumber > 1 ? 'bg-amber-500' : 'bg-sky-500'}`} />
                                    {index < asset.episodes.length - 1 ? <span className="mt-1 h-full w-px bg-border/80" /> : null}
                                  </div>
                                  <div className="flex-1 rounded-xl border border-border/70 bg-background px-3 py-3">
                                    <div className="flex flex-wrap items-center justify-between gap-2">
                                      <p className="text-sm font-medium">
                                        Episode #{episode.episodeNumber} {episode.episodeNumber > 1 ? 'reappeared' : 'detected'}
                                      </p>
                                      <span className="text-xs text-muted-foreground">
                                        {new Date(episode.firstSeenAt).toLocaleString()}
                                      </span>
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
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}
