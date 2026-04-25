import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { AlertTriangle, CheckCircle2, Clock3, ShieldAlert } from 'lucide-react'
import type { TechnicalManagerDashboardSummary } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { MetricInfoTooltip } from '@/components/features/dashboard/MetricInfoTooltip'
import { formatSoftwareOwnerRoutingDetail } from '@/components/features/remediation/remediation-utils'
import { formatDate, formatDateTime, startCase } from '@/lib/formatting'

type Props = {
  summary: TechnicalManagerDashboardSummary
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

export function TechnicalManagerOverview({ summary, isLoading }: Props) {
  const [now] = useState(() => Date.now())
  const overdueApprovedTasks = summary.approvedPatchingTasks.filter((item) => new Date(item.dueDate).getTime() < now).length

  return (
    <section className="space-y-6 pb-4">
      <Card className="overflow-hidden rounded-[2rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--chart-3)_12%,var(--background)),var(--card)_52%,var(--background))] shadow-[0_28px_70px_-48px_rgba(0,0,0,0.55)]">
        <CardContent className="p-6 sm:p-8">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.45fr)_minmax(22rem,1fr)]">
            <div className="space-y-4">
              <Badge variant="outline" className="rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-primary">
                Technical manager view
              </Badge>
              <div>
                <h1 className="text-3xl font-semibold tracking-[-0.06em] sm:text-4xl">
                  Patch execution and drift across the tenant
                </h1>
                <p className="mt-3 max-w-3xl text-base leading-7 text-muted-foreground">
                  Focus on approved patching work that must move, devices falling out of patch cadence, and approvals that could block execution.
                </p>
              </div>
            </div>

            <div className="grid gap-3 rounded-[1.5rem] border border-border/70 bg-background/45 p-5 backdrop-blur-sm sm:grid-cols-2">
              <Metric icon={CheckCircle2} label="Approved patching tasks" tooltip="A patching task represents approved software remediation work that is ready for execution. It reflects work that should now move through normal technical delivery." value={summary.approvedPatchingTasks.length} tone="text-chart-3" />
              <Metric icon={Clock3} label="Past due" tooltip="Past due means approved patch work has outlived its expected completion window. This is a leading signal of patching routine breakdown." value={overdueApprovedTasks} tone="text-destructive" />
              <Metric
                icon={AlertTriangle}
                label="Missed maintenance"
                tooltip="Missed maintenance means the planned patch window date has passed but PatchHound still sees open vulnerabilities in scope for that approved patching work."
                value={summary.missedMaintenanceWindowCount}
                tone="text-destructive"
                href={{
                  to: '/remediation',
                  search: {
                    page: 1,
                    pageSize: 25,
                    search: '',
                    criticality: '',
                    outcome: '',
                    approvalStatus: '',
                    decisionState: '',
                    missedMaintenanceWindow: true,
                  },
                }}
              />
              <Metric icon={ShieldAlert} label="Devices with 90+ day vulns" tooltip="This highlights long-lived exposure. The 90-day threshold is meant to catch devices that appear to be falling outside normal patching cadence." value={summary.devicesWithAgedVulnerabilities.length} tone="text-primary" />
              <Metric icon={AlertTriangle} label="Pending approvals" tooltip="Pending approvals are unresolved governance steps that can block technical remediation from proceeding, even when engineering capacity exists." value={summary.approvalTasksRequiringAttention.length} tone="text-chart-4" />
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.05fr)_minmax(0,0.95fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <CardTitle>Approved patching tasks</CardTitle>
            <CardDescription>
              Current approved patch work across software titles and owner teams.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {summary.approvedPatchingTasks.length === 0 ? (
              <EmptyState text={isLoading ? 'Loading approved patching tasks…' : 'No approved patching tasks are currently open.'} />
            ) : (
              summary.approvedPatchingTasks.map((item) => (
                <div key={item.patchingTaskId} className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="outline" className={`rounded-full ${severityTone(item.highestSeverity)}`}>
                          {item.highestSeverity}
                        </Badge>
                        <span className="text-xs text-muted-foreground">{item.affectedDeviceCount} affected devices</span>
                      </div>
                      <Link
                        to="/remediation/cases/$caseId"
                        params={{ caseId: item.remediationCaseId }}
                        className="mt-2 block text-base font-medium tracking-tight hover:text-primary"
                      >
                        {startCase(item.softwareName)}
                      </Link>
                      <p className="mt-1 text-sm text-muted-foreground">
                        {formatSoftwareOwnerRoutingDetail(item.ownerTeamName, item.ownerAssignmentSource)}
                      </p>
                    </div>
                    <div className="text-right text-sm text-muted-foreground">
                      <div className="flex justify-end">
                        <Badge variant="outline" className="rounded-full">
                          {item.ownerAssignmentSource}
                        </Badge>
                      </div>
                      <div>Approved {formatDateTime(item.approvedAt)}</div>
                      <div className="mt-1">
                        Maintenance {item.maintenanceWindowDate ? formatDate(item.maintenanceWindowDate) : 'Not scheduled'}
                      </div>
                      <div className="mt-1">Due {formatDate(item.dueDate)}</div>
                    </div>
                  </div>
                </div>
              ))
            )}
          </CardContent>
        </Card>

        <div className="space-y-4">
          <Card className="rounded-[1.6rem] border-border/70">
            <CardHeader>
              <CardTitle>Devices with 90+ day published vulnerabilities</CardTitle>
              <CardDescription>
                Devices carrying long-lived published vulnerabilities that may have slipped out of normal patching routines.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {summary.devicesWithAgedVulnerabilities.length === 0 ? (
                <EmptyState text={isLoading ? 'Loading devices…' : 'No devices with 90+ day published vulnerabilities were found.'} />
              ) : (
                summary.devicesWithAgedVulnerabilities.map((item) => (
                  <div key={item.deviceAssetId} className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <Badge variant="outline" className={`rounded-full ${severityTone(item.highestSeverity)}`}>
                            {item.highestSeverity}
                          </Badge>
                          <Badge variant="outline" className="rounded-full">
                            {item.criticality}
                          </Badge>
                        </div>
                        <Link
                          to="/devices/$id"
                          params={{ id: item.deviceAssetId }}
                          className="mt-2 block text-base font-medium tracking-tight hover:text-primary"
                        >
                          {item.deviceName}
                        </Link>
                        <p className="mt-1 text-sm text-muted-foreground">
                          {item.oldVulnerabilityCount} vulnerabilities older than 90 days by publish date
                        </p>
                      </div>
                      <div className="text-right text-sm text-muted-foreground">
                        <div>Oldest published</div>
                        <div className="mt-1 font-medium text-foreground">{formatDate(item.oldestPublishedDate)}</div>
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
                  Pending approvals that could slow patch execution.
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
              {summary.approvalTasksRequiringAttention.length === 0 ? (
                <EmptyState text="No pending approval tasks currently require attention." />
              ) : (
                summary.approvalTasksRequiringAttention.map((item) => (
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
                      </div>
                    </div>
                  </div>
                ))
              )}
            </CardContent>
          </Card>
        </div>
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
  href,
}: {
  icon: typeof ShieldAlert
  label: string
  tooltip: string
  value: number
  tone: string
  href?: {
    to: '/remediation'
    search: {
      page: number
      pageSize: number
      search: string
      criticality: string
      outcome: string
      approvalStatus: string
      decisionState: string
      missedMaintenanceWindow: boolean
    }
  }
}) {
  const content = (
    <div className="rounded-[1.2rem] border border-border/60 bg-card/70 px-4 py-4">
      <div className={`flex items-center gap-2 text-xs uppercase tracking-[0.18em] ${tone}`}>
        <Icon className="size-3.5" />
        {label}
        <MetricInfoTooltip content={tooltip} />
      </div>
      <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{value}</div>
    </div>
  )

  if (!href) {
    return content
  }

  return (
    <Link
      to={href.to}
      search={href.search}
      className="block rounded-[1.2rem] transition hover:-translate-y-0.5 hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {content}
    </Link>
  )
}

function EmptyState({ text }: { text: string }) {
  return (
    <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
      {text}
    </div>
  )
}
