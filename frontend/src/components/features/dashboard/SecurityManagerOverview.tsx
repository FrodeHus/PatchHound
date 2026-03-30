import { Link } from '@tanstack/react-router'
import { AlertTriangle, CheckCircle2, ClipboardCheck, ShieldAlert } from 'lucide-react'
import type { DashboardSummary, SecurityManagerDashboardSummary } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { MetricInfoTooltip } from '@/components/features/dashboard/MetricInfoTooltip'
import { RiskScoreCard } from '@/components/features/dashboard/RiskScoreCard'
import { outcomeLabel } from '@/components/features/remediation/remediation-utils'
import { formatDate, formatDateTime, startCase } from '@/lib/formatting'

type Props = {
  summary: DashboardSummary
  managerSummary: SecurityManagerDashboardSummary
  isLoading?: boolean
}

function severityTone(severity: string) {
  if (severity === 'Critical') return 'border-destructive/25 bg-destructive/10 text-destructive'
  if (severity === 'High') return 'border-primary/25 bg-primary/10 text-primary'
  if (severity === 'Medium') return 'border-chart-4/25 bg-chart-4/10 text-chart-4'
  return 'border-border/70 bg-muted/50 text-muted-foreground'
}

function attentionTone(state: string) {
  if (state === 'Overdue') return 'border-destructive/25 bg-destructive/10 text-destructive'
  if (state === 'NearExpiry') return 'border-chart-4/25 bg-chart-4/10 text-chart-4'
  return 'border-border/70 bg-muted/50 text-muted-foreground'
}

export function SecurityManagerOverview({ summary, managerSummary, isLoading }: Props) {
  return (
    <section className="space-y-6 pb-4">
      <Card className="overflow-hidden rounded-[2rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_14%,var(--background)),var(--card)_52%,var(--background))] shadow-[0_28px_70px_-48px_rgba(0,0,0,0.55)]">
        <CardContent className="p-6 sm:p-8">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.45fr)_minmax(22rem,1fr)]">
            <div className="space-y-4">
              <Badge variant="outline" className="rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-primary">
                Security manager view
              </Badge>
              <div>
                <h1 className="text-3xl font-semibold tracking-[-0.06em] sm:text-4xl">
                  Policy and remediation oversight for this tenant
                </h1>
                <p className="mt-3 max-w-3xl text-base leading-7 text-muted-foreground">
                  Use this view to track current risk, watch approval pressure, and review the latest approved exceptions without leaving the dashboard.
                </p>
              </div>
            </div>

            <div className="grid gap-3 rounded-[1.5rem] border border-border/70 bg-background/45 p-5 backdrop-blur-sm sm:grid-cols-2">
              <Metric
                icon={ShieldAlert}
                label="Critical backlog"
                tooltip="Backlog here means unresolved exposure that is still present. Critical backlog is the part of that exposure sitting in the highest severity band and therefore most likely to require executive or governance attention."
                value={summary.vulnerabilitiesBySeverity.Critical ?? 0}
                tone="text-destructive"
              />
              <Metric
                icon={AlertTriangle}
                label="Overdue actions"
                tooltip="Overdue actions are remediation items that have moved past their expected completion date. They are a proxy for execution strain and control slippage."
                value={summary.overdueTaskCount}
                tone="text-primary"
              />
              <Metric
                icon={ClipboardCheck}
                label="Pending approvals"
                tooltip="Approval tasks are decisions that need formal review before the organization accepts risk or chooses an alternate mitigation path. Pending items near expiry create governance pressure."
                value={managerSummary.approvalTasksRequiringAttention.length}
                tone="text-chart-4"
              />
              <Metric
                icon={CheckCircle2}
                label="Approved exceptions"
                tooltip="Exceptions are approved departures from normal patching or remediation, such as risk acceptance or alternate mitigation. This metric reflects the recent flow of such approved exceptions."
                value={managerSummary.recentApprovedDecisions.length}
                tone="text-chart-3"
              />
            </div>
          </div>
        </CardContent>
      </Card>

      <RiskScoreCard isLoading={isLoading} />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader className="flex flex-row items-start justify-between gap-3">
            <div>
              <CardTitle>Recent approved exception decisions</CardTitle>
              <CardDescription>
                The latest approved risk acceptance and alternate mitigation decisions across tracked software.
              </CardDescription>
            </div>
            <Button
              size="sm"
              variant="outline"
              render={<Link to="/remediation" search={{ page: 1, pageSize: 25, search: '', criticality: '', outcome: '', approvalStatus: 'Approved', decisionState: '' }} />}
            >
              Full list
            </Button>
          </CardHeader>
          <CardContent className="space-y-3">
            {managerSummary.recentApprovedDecisions.length === 0 ? (
              <EmptyState text="No approved risk acceptance or alternate mitigation decisions were found." />
            ) : (
              managerSummary.recentApprovedDecisions.map((item) => (
                <div key={item.decisionId} className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="outline" className={`rounded-full ${severityTone(item.highestSeverity)}`}>
                          {item.highestSeverity}
                        </Badge>
                        <span className="text-xs text-muted-foreground">{item.vulnerabilityCount} open vulnerabilities</span>
                      </div>
                      <Link
                        to="/software/$id/remediation"
                        params={{ id: item.tenantSoftwareId }}
                        className="mt-2 block text-base font-medium tracking-tight hover:text-primary"
                      >
                        {startCase(item.softwareName)}
                      </Link>
                      <p className="mt-1 text-sm text-muted-foreground">
                        {outcomeLabel(item.outcome)}
                      </p>
                      {item.justification ? (
                        <p className="mt-2 line-clamp-2 text-sm text-muted-foreground">{item.justification}</p>
                      ) : null}
                    </div>
                    <div className="text-right text-sm text-muted-foreground">
                      <div>Approved {formatDateTime(item.approvedAt)}</div>
                      {item.expiryDate ? <div className="mt-1">Expires {formatDate(item.expiryDate)}</div> : null}
                    </div>
                  </div>
                </div>
              ))
            )}
          </CardContent>
        </Card>

        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader className="flex flex-row items-start justify-between gap-3">
            <div>
              <CardTitle>Approval tasks requiring attention</CardTitle>
              <CardDescription>
                Pending approvals ordered by urgency so expiring items surface first.
              </CardDescription>
            </div>
            <Button
              size="sm"
              variant="outline"
              render={<Link to="/approvals" search={{ page: 1, pageSize: 25, status: 'Pending', type: '', search: '', showRead: false }} />}
            >
              Approval inbox
            </Button>
          </CardHeader>
          <CardContent className="space-y-3">
            {managerSummary.approvalTasksRequiringAttention.length === 0 ? (
              <EmptyState text="No pending approval tasks currently require attention." />
            ) : (
              managerSummary.approvalTasksRequiringAttention.map((item) => (
                <div key={item.approvalTaskId} className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="outline" className={`rounded-full ${attentionTone(item.attentionState)}`}>
                          {item.attentionState === 'NearExpiry' ? 'Due soon' : item.attentionState}
                        </Badge>
                        <Badge variant="outline" className={`rounded-full ${severityTone(item.highestSeverity)}`}>
                          {item.highestSeverity}
                        </Badge>
                      </div>
                      <Link
                        to="/approvals/$id"
                        params={{ id: item.approvalTaskId }}
                        className="mt-2 block text-base font-medium tracking-tight hover:text-primary"
                      >
                          {startCase(item.softwareName)}
                      </Link>
                      <p className="mt-1 text-sm text-muted-foreground">
                        {item.approvalType} for {item.vulnerabilityCount} open vulnerabilities
                      </p>
                    </div>
                    <div className="text-right text-sm text-muted-foreground">
                      <div>Expires {formatDateTime(item.expiresAt)}</div>
                      {item.maintenanceWindowDate ? (
                        <div className="mt-1">Maintenance {formatDate(item.maintenanceWindowDate)}</div>
                      ) : null}
                      <div className="mt-1">Created {formatDate(item.createdAt)}</div>
                    </div>
                  </div>
                </div>
              ))
            )}
          </CardContent>
        </Card>
      </div>
    </section>
  )
}

function Metric({
  icon: Icon,
  label,
  tooltip,
  value,
  tone,
}: {
  icon: typeof ShieldAlert
  label: string
  tooltip: string
  value: number
  tone: string
}) {
  return (
    <div className="rounded-[1.2rem] border border-border/60 bg-card/70 px-4 py-4">
      <div className={`flex items-center gap-2 text-xs uppercase tracking-[0.18em] ${tone}`}>
        <Icon className="size-3.5" />
        {label}
        <MetricInfoTooltip content={tooltip} />
      </div>
      <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{value}</div>
    </div>
  )
}

function EmptyState({ text }: { text: string }) {
  return (
    <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
      {text}
    </div>
  )
}
