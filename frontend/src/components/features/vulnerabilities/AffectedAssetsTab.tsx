import type { AffectedAsset } from '@/api/vulnerabilities.schemas'

type AffectedAssetsTabProps = {
  assets: AffectedAsset[]
}

export function AffectedAssetsTab({ assets }: AffectedAssetsTabProps) {
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
            {assets.map((asset) => (
              <tr key={asset.assetId} className="border-b border-border/60">
                <td className="py-2 pr-2">
                  <div className="space-y-1">
                    <p>{asset.assetName}</p>
                    {asset.possibleCorrelatedSoftware.length > 0 ? (
                      <p className="text-xs text-amber-700">
                        Reappearance overlaps with: {asset.possibleCorrelatedSoftware.join(', ')}
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
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
