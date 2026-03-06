import type { TopVulnerability } from '@/api/useDashboard'

type CriticalVulnerabilitiesProps = {
  items: TopVulnerability[]
}

export function CriticalVulnerabilities({ items }: CriticalVulnerabilitiesProps) {
  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Top Critical Vulnerabilities</h2>
      <div className="mt-3 overflow-x-auto">
        <table className="w-full min-w-[520px] border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">External ID</th>
              <th className="py-2 pr-2">Title</th>
              <th className="py-2 pr-2">Severity</th>
              <th className="py-2 pr-2">Assets</th>
              <th className="py-2 pr-2">Age (days)</th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 ? (
              <tr>
                <td colSpan={5} className="py-3 text-muted-foreground">
                  No critical vulnerabilities found.
                </td>
              </tr>
            ) : (
              items.map((item) => (
                <tr key={item.id} className="border-b border-border/60">
                  <td className="py-2 pr-2 font-medium">{item.externalId}</td>
                  <td className="py-2 pr-2">{item.title}</td>
                  <td className="py-2 pr-2">{item.severity}</td>
                  <td className="py-2 pr-2">{item.affectedAssetCount}</td>
                  <td className="py-2 pr-2">{item.daysSincePublished}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
