import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'

type RemediationVelocityProps = {
  averageDays: number
  vulnerabilitiesBySeverity: Record<string, number>
}

type VelocityPoint = {
  severity: string
  volume: number
}

function getChartData(vulnerabilitiesBySeverity: Record<string, number>): VelocityPoint[] {
  return ['Low', 'Medium', 'High', 'Critical'].map((severity) => ({
    severity,
    volume: vulnerabilitiesBySeverity[severity] ?? 0,
  }))
}

export function RemediationVelocity({ averageDays, vulnerabilitiesBySeverity }: RemediationVelocityProps) {
  const chartData = getChartData(vulnerabilitiesBySeverity)

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="flex items-end justify-between">
        <h2 className="text-lg font-semibold">Remediation Velocity</h2>
        <p className="text-sm text-muted-foreground">Avg: {Number(averageDays.toFixed(1))} days</p>
      </div>
      <div className="mt-3 h-[240px] w-full">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData}>
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis dataKey="severity" />
            <YAxis allowDecimals={false} />
            <Tooltip />
            <Bar dataKey="volume" fill="var(--primary)" radius={[4, 4, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </section>
  )
}
