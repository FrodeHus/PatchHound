import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { TrendData } from '@/api/useDashboard'

type TrendChartProps = {
  data: TrendData
}

type ChartPoint = {
  date: string
  Low: number
  Medium: number
  High: number
  Critical: number
}

function formatChartData(data: TrendData): ChartPoint[] {
  const map = new Map<string, ChartPoint>()

  data.items.forEach((item) => {
    const existing = map.get(item.date) ?? {
      date: item.date,
      Low: 0,
      Medium: 0,
      High: 0,
      Critical: 0,
    }

    if (item.severity === 'Low' || item.severity === 'Medium' || item.severity === 'High' || item.severity === 'Critical') {
      existing[item.severity] = item.count
    }

    map.set(item.date, existing)
  })

  return Array.from(map.values())
}

export function TrendChart({ data }: TrendChartProps) {
  const points = formatChartData(data)

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Vulnerability Trends (12 months)</h2>
      <div className="mt-3 h-[280px] w-full">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={points}>
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis dataKey="date" tick={{ fontSize: 12 }} />
            <YAxis allowDecimals={false} tick={{ fontSize: 12 }} />
            <Tooltip />
            <Legend />
            <Line type="monotone" dataKey="Low" stroke="var(--chart-2)" strokeWidth={2} dot={false} />
            <Line type="monotone" dataKey="Medium" stroke="var(--chart-4)" strokeWidth={2} dot={false} />
            <Line type="monotone" dataKey="High" stroke="var(--chart-1)" strokeWidth={2} dot={false} />
            <Line type="monotone" dataKey="Critical" stroke="var(--destructive)" strokeWidth={2} dot={false} />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </section>
  )
}
