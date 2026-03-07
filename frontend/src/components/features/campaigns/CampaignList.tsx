import { Link } from '@tanstack/react-router'
import type { Campaign } from '@/api/campaigns.schemas'

type CampaignListProps = {
  items: Campaign[]
  totalCount: number
}

function progressPercent(total: number, completed: number): number {
  if (total <= 0) return 0
  return Math.round((completed / total) * 100)
}

export function CampaignList({ items, totalCount }: CampaignListProps) {
  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="mb-3 flex items-end justify-between">
        <h2 className="text-lg font-semibold">Campaigns</h2>
        <p className="text-xs text-muted-foreground">{totalCount} total</p>
      </div>

      <div className="space-y-3">
        {items.length === 0 ? <p className="text-sm text-muted-foreground">No campaigns found.</p> : null}
        {items.map((item) => {
          const percent = progressPercent(item.totalTasks, item.completedTasks)
          return (
            <article key={item.id} className="rounded-md border border-border/70 p-3">
              <div className="mb-2 flex items-start justify-between gap-3">
                <div>
                  <Link to="/campaigns/$id" params={{ id: item.id }} className="font-medium text-primary hover:underline">
                    {item.name}
                  </Link>
                  <p className="text-xs text-muted-foreground">{item.description ?? 'No description'}</p>
                </div>
                <span className="rounded-full border border-input px-2 py-0.5 text-xs">{item.status}</span>
              </div>

              <div className="mb-1 h-2 w-full overflow-hidden rounded-full bg-muted">
                <div className="h-full bg-primary" style={{ width: `${percent}%` }} />
              </div>
              <p className="text-xs text-muted-foreground">
                {item.completedTasks}/{item.totalTasks} tasks complete ({percent}%) | {item.vulnerabilityCount} vulnerabilities
              </p>
            </article>
          )
        })}
      </div>
    </section>
  )
}
