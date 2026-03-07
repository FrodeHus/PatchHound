type SlaComplianceCardProps = {
  percent: number
  overdueCount: number
  totalCount: number
}

export function SlaComplianceCard({ percent, overdueCount, totalCount }: SlaComplianceCardProps) {
  const boundedPercent = Math.max(0, Math.min(100, Number(percent.toFixed(1))))

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="flex items-end justify-between">
        <p className="text-sm text-muted-foreground">SLA Compliance</p>
        <p className="text-xl font-semibold">{boundedPercent}%</p>
      </div>
      <div className="mt-3 h-2 w-full overflow-hidden rounded-full bg-muted">
        <div className="h-full bg-primary transition-all" style={{ width: `${boundedPercent}%` }} />
      </div>
      <p className="mt-2 text-xs text-muted-foreground">
        {overdueCount} overdue of {totalCount} tracked remediation tasks.
      </p>
    </section>
  )
}
