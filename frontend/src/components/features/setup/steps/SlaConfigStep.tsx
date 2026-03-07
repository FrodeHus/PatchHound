export function SlaConfigStep() {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">SLA Defaults</h2>
      <ul className="list-disc pl-5 text-sm text-muted-foreground">
        <li>Critical: 7 days</li>
        <li>High: 30 days</li>
        <li>Medium: 90 days</li>
        <li>Low: 180 days</li>
      </ul>
    </section>
  )
}
