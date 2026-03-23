import type { SoftwareRemediationSummary } from '@/api/remediation.schemas'
import { toneText, type Tone } from '@/lib/tone-classes'

type RemediationSummaryCardsProps = {
  summary: SoftwareRemediationSummary
}

type StatCard = {
  label: string
  value: number
  tone: Tone
}

export function RemediationSummaryCards({ summary }: RemediationSummaryCardsProps) {
  const cards: StatCard[] = [
    { label: 'Total', value: summary.totalVulnerabilities, tone: 'neutral' },
    { label: 'Critical', value: summary.criticalCount, tone: 'danger' },
    { label: 'High', value: summary.highCount, tone: 'warning' },
    { label: 'Medium', value: summary.mediumCount, tone: 'info' },
    { label: 'Low', value: summary.lowCount, tone: 'neutral' },
    { label: 'Known Exploits', value: summary.withKnownExploit, tone: 'danger' },
    { label: 'Active Alerts', value: summary.withActiveAlert, tone: 'warning' },
    { label: 'Pending Tasks', value: summary.pendingRemediationTasks, tone: 'info' },
    { label: 'Risk Accepted', value: summary.riskAcceptedCount, tone: 'success' },
  ]

  return (
    <div className="grid grid-cols-3 gap-3 sm:grid-cols-5 lg:grid-cols-9">
      {cards.map((card) => (
        <div
          key={card.label}
          className="rounded-xl border border-border/70 bg-card p-3 text-center"
        >
          <p className={`text-2xl font-semibold tabular-nums ${toneText(card.tone)}`}>
            {card.value}
          </p>
          <p className="mt-0.5 text-xs text-muted-foreground">{card.label}</p>
        </div>
      ))}
    </div>
  )
}
