import type { DecisionSummary } from '@/api/remediation.schemas'
import { toneBadge, toneText, type Tone } from '@/lib/tone-classes'

type RemediationSummaryCardsProps = {
  summary: DecisionSummary
}

export function RemediationSummaryCards({ summary }: RemediationSummaryCardsProps) {
  const exposureMix = [
    { label: 'Critical', value: summary.criticalCount, tone: 'danger' as Tone },
    { label: 'High', value: summary.highCount, tone: 'warning' as Tone },
    { label: 'Medium', value: summary.mediumCount, tone: 'info' as Tone },
    { label: 'Low', value: summary.lowCount, tone: 'neutral' as Tone },
  ]
  const elevatedExposure = summary.criticalCount + summary.highCount
  const threatSignals = summary.withKnownExploit + summary.withActiveAlert

  return (
    <div className="grid gap-3 lg:grid-cols-3">
      <section className="rounded-2xl border border-border/70 bg-card p-4">
        <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
          Exposure
        </p>
        <div className="mt-3 flex items-end justify-between gap-3">
          <div>
            <p className="text-3xl font-semibold tracking-[-0.04em] tabular-nums">
              {summary.totalVulnerabilities}
            </p>
            <p className="text-sm text-muted-foreground">open vulnerabilities</p>
          </div>
          <span
            className={`inline-flex rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${toneBadge(
              elevatedExposure > 0 ? 'warning' : 'neutral'
            )}`}
          >
            {elevatedExposure} critical or high
          </span>
        </div>
        <div className="mt-4 grid grid-cols-4 gap-2">
          {exposureMix.map((item) => (
            <div key={item.label} className="rounded-xl border border-border/60 bg-background/60 px-3 py-2 text-center">
              <p className={`text-lg font-semibold tabular-nums ${toneText(item.tone)}`}>
                {item.value}
              </p>
              <p className="mt-0.5 text-[11px] text-muted-foreground">{item.label}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="rounded-2xl border border-border/70 bg-card p-4">
        <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
          Threat activity
        </p>
        <div className="mt-3 flex items-end justify-between gap-3">
          <div>
            <p className="text-3xl font-semibold tracking-[-0.04em] tabular-nums">
              {threatSignals}
            </p>
            <p className="text-sm text-muted-foreground">active threat signals</p>
          </div>
        </div>
        <div className="mt-4 grid grid-cols-2 gap-2">
          <div className="rounded-xl border border-border/60 bg-background/60 px-3 py-3">
            <p className="text-lg font-semibold tabular-nums text-tone-danger-foreground">
              {summary.withKnownExploit}
            </p>
            <p className="mt-0.5 text-[11px] text-muted-foreground">known exploits</p>
          </div>
          <div className="rounded-xl border border-border/60 bg-background/60 px-3 py-3">
            <p className="text-lg font-semibold tabular-nums text-tone-warning-foreground">
              {summary.withActiveAlert}
            </p>
            <p className="mt-0.5 text-[11px] text-muted-foreground">active alerts</p>
          </div>
        </div>
      </section>

      <section className="rounded-2xl border border-border/70 bg-card p-4">
        <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
          Decision context
        </p>
        <div className="mt-3 space-y-3">
          <div className="rounded-xl border border-border/60 bg-background/60 px-3 py-3">
            <p className="text-sm font-medium">What needs a decision now</p>
            <p className="mt-1 text-sm text-muted-foreground">
              Focus on the highest-severity vulnerabilities and any threat signals that suggest active exploitation or ongoing alerts.
            </p>
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div className="rounded-xl border border-border/60 bg-background/60 px-3 py-3">
              <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                Critical + High
              </p>
              <p className="mt-1 text-lg font-semibold tabular-nums">
                {summary.criticalCount + summary.highCount}
              </p>
            </div>
            <div className="rounded-xl border border-border/60 bg-background/60 px-3 py-3">
              <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                Threat-driven
              </p>
              <p className="mt-1 text-lg font-semibold tabular-nums">
                {threatSignals}
              </p>
            </div>
          </div>
        </div>
      </section>
    </div>
  )
}
