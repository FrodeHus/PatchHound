import { useState } from 'react'
import type { ApprovalTaskDetail as ApprovalTaskDetailType } from '@/api/approval-tasks.schemas'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import {
  AuditTimeline,
  type AuditTimelineEvent,
} from '@/components/features/audit/AuditTimeline'
import {
  outcomeLabel,
  outcomeTone,
} from '@/components/features/remediation/remediation-utils'
import { toneBadge } from '@/lib/tone-classes'
import { formatDate, startCase } from '@/lib/formatting'
import { ApprovalStatusBadge } from './ApprovalBadge'
import { ApprovalExpiryCountdown } from './ApprovalExpiryCountdown'
import { CheckCircle, XCircle, Eye, AlertTriangle, MessageSquare, ShieldCheck } from 'lucide-react'

type Props = {
  data: ApprovalTaskDetailType
  onResolve: (action: 'approve' | 'deny', justification?: string, maintenanceWindowDate?: string) => void
  onMarkRead: () => void
  onVulnPageChange: (page: number) => void
  onDevicePageChange: (page: number) => void
  onDeviceVersionChange: (version: string) => void
}

function severityTone(severity: string) {
  switch (severity) {
    case 'Critical':
      return 'danger' as const
    case 'High':
      return 'warning' as const
    case 'Medium':
      return 'neutral' as const
    case 'Low':
      return 'info' as const
    default:
      return 'neutral' as const
  }
}

function riskBandTone(band: string) {
  switch (band) {
    case 'Critical':
      return 'danger' as const
    case 'High':
      return 'warning' as const
    case 'Medium':
      return 'neutral' as const
    case 'Low':
      return 'info' as const
    default:
      return 'neutral' as const
  }
}

function normalizeVersion(version: string | null) {
  return version?.trim() ?? ''
}

function formatVersion(version: string | null) {
  return version && version.trim().length > 0 ? version : 'Unknown version'
}

function toDateInputValue(value?: string | null) {
  if (!value) return ''
  return value.slice(0, 10)
}

function toIsoDateBoundary(value: string) {
  if (!value) return undefined
  return `${value}T00:00:00Z`
}

export function ApprovalTaskDetail({
  data,
  onResolve,
  onMarkRead,
  onVulnPageChange,
  onDevicePageChange,
  onDeviceVersionChange,
}: Props) {
  const [justification, setJustification] = useState('')
  const [maintenanceWindowDate, setMaintenanceWindowDate] = useState(toDateInputValue(data.maintenanceWindowDate))
  const [resolveAction, setResolveAction] = useState<'approve' | 'deny' | null>(
    null
  )
  const isPending = data.status === 'Pending'
  const justificationRequired = data.requiresJustification
  const maintenanceWindowRequired = isPending && data.outcome === 'ApprovedForPatching'
  const vulnerabilityCount = data.vulnerabilities.totalCount
  const affectedDeviceCount =
    data.deviceVersionCohorts.reduce((sum, cohort) => sum + cohort.deviceCount, 0) ||
    data.devices?.totalCount ||
    0
  const auditEvents: AuditTimelineEvent[] = data.auditTrail.map((entry, index) => ({
    id: `${entry.timestamp}-${entry.action}-${index}`,
    action: entry.action,
    title: buildApprovalTimelineTitle(entry.action, entry.userDisplayName),
    description: entry.justification
      ? `Justification: ${entry.justification}`
      : undefined,
    timestamp: entry.timestamp,
  }))

  function handleResolve(action: 'approve' | 'deny') {
    if (justificationRequired && !justification.trim()) {
      setResolveAction(action)
      return
    }
    if (action === 'approve' && maintenanceWindowRequired && !maintenanceWindowDate) {
      setResolveAction(action)
      return
    }
    onResolve(action, justification.trim() || undefined, action === 'approve' ? toIsoDateBoundary(maintenanceWindowDate) : undefined)
  }

  return (
    <div className="grid gap-5 lg:grid-cols-[1fr_320px]">
      <section className="min-w-0 space-y-5">
      <header className="rounded-[28px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_48%),var(--color-card)] p-5">
        <div className="space-y-3">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
            Approval task
          </p>
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">
              Remediation Approval: {startCase(data.softwareName)}
            </h1>
            {!isPending && !data.readAt ? (
              <Button
                variant="ghost"
                size="icon"
                onClick={onMarkRead}
                title="Mark as read"
                aria-label="Mark as read"
                className="size-8 rounded-full border border-border/70"
              >
                <Eye className="size-4" />
              </Button>
            ) : null}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span
              className={`inline-flex rounded-full border px-2.5 py-1 text-[11px] font-medium ${toneBadge(severityTone(data.criticality))}`}
            >
              Severity: {data.criticality}
            </span>
            <ApprovalStatusBadge status={data.status} />
            {isPending ? (
              <ApprovalExpiryCountdown expiresAt={data.expiresAt} compact />
            ) : null}
          </div>
        </div>
      </header>

      {isPending ? (
        <section className="rounded-2xl border border-border/70 bg-card p-5">
          <div className="flex items-start justify-between">
            <div className="flex items-center gap-2">
              <ShieldCheck className="size-5 text-muted-foreground" />
              <h2 className="text-lg font-semibold tracking-[-0.02em]">
                Reviewer Verdict
              </h2>
            </div>
            <CheckCircle className="size-10 text-muted-foreground/30" />
          </div>

          <div className="mt-4 space-y-3">
            <div className="flex items-center justify-between">
              <label className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
                Approval justification
              </label>
              {justificationRequired ? (
                <span className="inline-flex rounded-full border border-border/70 bg-background/70 px-2.5 py-1 text-[11px] text-muted-foreground">
                  Required
                </span>
              ) : null}
            </div>

            {maintenanceWindowRequired ? (
              <div className="space-y-2">
                <label className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  Maintenance window date
                </label>
                <Input
                  type="date"
                  value={maintenanceWindowDate}
                  onChange={(e) => setMaintenanceWindowDate(e.target.value)}
                />
                <p className="text-xs text-muted-foreground">
                  The technical manager sets when the approved patch is expected to be in place.
                </p>
              </div>
            ) : null}

            <Textarea
              placeholder="Provide technical rationale for this decision..."
              value={justification}
              onChange={(e) => setJustification(e.target.value)}
              rows={4}
            />

            {resolveAction && justificationRequired && !justification.trim() ? (
              <p className="flex items-center gap-1.5 text-sm text-tone-danger-foreground">
                <AlertTriangle className="size-3.5" />
                Justification is required to {resolveAction} this task.
              </p>
            ) : null}
            {resolveAction === 'approve' && maintenanceWindowRequired && !maintenanceWindowDate ? (
              <p className="flex items-center gap-1.5 text-sm text-tone-danger-foreground">
                <AlertTriangle className="size-3.5" />
                Maintenance window date is required to approve this patching request.
              </p>
            ) : null}

            <div className="flex flex-wrap gap-3 pt-1">
              <Button onClick={() => handleResolve('approve')} className="min-w-[180px]">
                <CheckCircle className="mr-1.5 size-4" />
                Approve Remediation
              </Button>
              <Button variant="destructive" onClick={() => handleResolve('deny')} className="min-w-[180px]">
                <XCircle className="mr-1.5 size-4" />
                Deny Request
              </Button>
            </div>
          </div>
        </section>
      ) : null}

      <section className="rounded-[28px] border border-border/70 bg-card p-4">
        <Tabs defaultValue="justification" className="gap-4">
          <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
            <TabsTrigger value="justification" className="rounded-lg px-4 text-sm">
              Justification
            </TabsTrigger>
            <TabsTrigger value="vulnerabilities" className="rounded-lg px-4 text-sm">
              Vulnerabilities ({vulnerabilityCount})
            </TabsTrigger>
            <TabsTrigger value="devices" className="rounded-lg px-4 text-sm">
              Affected Devices ({affectedDeviceCount})
            </TabsTrigger>
            <TabsTrigger value="timeline" className="rounded-lg px-4 text-sm">
              Timeline
            </TabsTrigger>
          </TabsList>

          <TabsContent value="justification" className="space-y-4 pt-1">
            <section className="rounded-2xl border border-border/70 bg-background/60 p-5">
              <h2 className="mb-3 text-xs uppercase tracking-[0.14em] text-muted-foreground">
                Selected decision
              </h2>
              <p className="text-sm leading-relaxed text-foreground/90">
                This approval task asks you to review the decision to{' '}
                <span className="font-medium">{outcomeLabel(data.outcome).toLowerCase()}</span>{' '}
                for this software scope.
              </p>
            </section>

            <section className="rounded-2xl border border-border/70 bg-background/60 p-5">
              <h2 className="mb-3 text-xs uppercase tracking-[0.14em] text-muted-foreground">
                Decision justification
              </h2>
              <p className="text-sm leading-relaxed text-foreground/90">
                {data.justification || 'No justification was provided for this decision.'}
              </p>
            </section>

            {data.recommendations.length > 0 ? (
              <section className="rounded-2xl border border-border/70 bg-background/40 p-5">
                <h2 className="mb-4 text-xs uppercase tracking-[0.14em] text-muted-foreground">
                  Analyst recommendations
                </h2>
                <div className="space-y-3">
                  {data.recommendations.map((rec) => (
                    <div
                      key={rec.id}
                      className="rounded-xl border border-border/60 bg-background/70 p-4"
                    >
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2">
                          <MessageSquare className="size-4 text-muted-foreground" />
                          <span className="text-xs font-medium">
                            {outcomeLabel(rec.recommendedOutcome)}
                          </span>
                          {rec.priorityOverride ? (
                            <span className="text-[10px] text-muted-foreground">
                              Priority: {rec.priorityOverride}
                            </span>
                          ) : null}
                        </div>
                        <span className="text-[10px] text-muted-foreground">
                          {formatDate(rec.createdAt)}
                        </span>
                      </div>
                      <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
                        {rec.rationale}
                      </p>
                    </div>
                  ))}
                </div>
              </section>
            ) : null}
          </TabsContent>

          <TabsContent value="vulnerabilities" className="space-y-4 pt-1">
            <section className="rounded-2xl border border-border/70 bg-background/40 p-5">
              <h2 className="mb-4 text-xs uppercase tracking-[0.14em] text-muted-foreground">
                Vulnerabilities ({data.vulnerabilities.totalCount})
              </h2>
              <div className="overflow-hidden rounded-xl border border-border/70">
                <table className="min-w-full divide-y divide-border/70 text-sm">
                  <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
                    <tr>
                      <th className="px-4 py-3">CVE</th>
                      <th className="px-4 py-3">Title</th>
                      <th className="px-4 py-3">Severity</th>
                      <th className="px-4 py-3">CVSS</th>
                      <th className="px-4 py-3">EPSS</th>
                      <th className="px-4 py-3">Exploited</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/60 bg-background">
                    {data.vulnerabilities.items.length === 0 ? (
                      <tr>
                        <td
                          className="px-4 py-6 text-sm text-muted-foreground"
                          colSpan={6}
                        >
                          No vulnerabilities found.
                        </td>
                      </tr>
                    ) : (
                      data.vulnerabilities.items.map((vuln) => (
                        <tr key={vuln.tenantVulnerabilityId} className="align-top">
                          <td className="px-4 py-3 font-mono text-xs">
                            {vuln.externalId}
                          </td>
                          <td className="max-w-xs truncate px-4 py-3">
                            {vuln.title}
                          </td>
                          <td className="px-4 py-3">
                            <span
                              className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(vuln.vendorSeverity))}`}
                            >
                              {vuln.vendorSeverity}
                            </span>
                          </td>
                          <td className="px-4 py-3 tabular-nums text-muted-foreground">
                            {vuln.vendorScore?.toFixed(1) ?? '-'}
                          </td>
                          <td className="px-4 py-3 tabular-nums text-muted-foreground">
                            {vuln.epssScore ? `${(vuln.epssScore * 100).toFixed(1)}%` : '-'}
                          </td>
                          <td className="px-4 py-3">
                            {vuln.knownExploited ? (
                              <span className="inline-flex items-center gap-1 text-xs text-tone-danger-foreground">
                                <AlertTriangle className="size-3" />
                                Yes
                              </span>
                            ) : (
                              <span className="text-xs text-muted-foreground">No</span>
                            )}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
              {data.vulnerabilities.totalCount > data.vulnerabilities.pageSize ? (
                <div className="mt-3 flex items-center justify-between gap-3">
                  <p className="text-sm text-muted-foreground">
                    Page {data.vulnerabilities.page} of{' '}
                    {Math.ceil(
                      data.vulnerabilities.totalCount / data.vulnerabilities.pageSize
                    )}
                  </p>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={data.vulnerabilities.page <= 1}
                      onClick={() => onVulnPageChange(data.vulnerabilities.page - 1)}
                    >
                      Previous
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={
                        data.vulnerabilities.page >=
                        Math.ceil(
                          data.vulnerabilities.totalCount / data.vulnerabilities.pageSize
                        )
                      }
                      onClick={() => onVulnPageChange(data.vulnerabilities.page + 1)}
                    >
                      Next
                    </Button>
                  </div>
                </div>
              ) : null}
            </section>

          </TabsContent>

          <TabsContent value="devices" className="pt-1">
            {data.devices ? (
              <section className="space-y-4 rounded-2xl border border-border/70 bg-background/40 p-5">
                {(() => {
                  const devices = data.devices
                  const selectedVersion = normalizeVersion(
                    devices.items[0]?.version
                    ?? data.deviceVersionCohorts[0]?.version
                    ?? null
                  )
                  const maxInstallCount = Math.max(
                    ...data.deviceVersionCohorts.map(item => item.activeInstallCount),
                    1
                  )

                  return (
                    <>
                      <div>
                        <h2 className="text-lg font-semibold">Affected devices</h2>
                        <p className="text-sm text-muted-foreground">
                          Version cohorts for the software in this approval decision.
                        </p>
                      </div>

                      {data.deviceVersionCohorts.length > 0 ? (
                        <div className="grid gap-3 lg:grid-cols-4">
                          {data.deviceVersionCohorts.map((cohort) => {
                            const versionKey = normalizeVersion(cohort.version)
                            const isSelected = versionKey === selectedVersion

                            return (
                              <button
                                key={versionKey || '__unknown__'}
                                type="button"
                                onClick={() => onDeviceVersionChange(versionKey)}
                                className={
                                  isSelected
                                    ? 'rounded-xl border border-primary/30 bg-primary/10 p-4 text-left'
                                    : 'rounded-xl border border-border/70 bg-background p-4 text-left hover:border-foreground/20 hover:bg-muted/20'
                                }
                              >
                                <div className="flex items-start justify-between gap-3">
                                  <div>
                                    <p className="text-sm font-medium">
                                      {formatVersion(cohort.version)}
                                    </p>
                                    <p className="mt-1 text-xs text-muted-foreground">
                                      {cohort.activeInstallCount} installs on {cohort.deviceCount} devices
                                    </p>
                                  </div>
                                  <div className="rounded-full bg-background/80 px-2 py-1 text-xs text-muted-foreground">
                                    {cohort.activeVulnerabilityCount} vuln
                                    {cohort.activeVulnerabilityCount === 1 ? '' : 's'}
                                  </div>
                                </div>
                                <div className="mt-4 h-2 overflow-hidden rounded-full bg-muted">
                                  <div
                                    className={
                                      cohort.activeVulnerabilityCount > 0
                                        ? 'h-full rounded-full bg-amber-400'
                                        : 'h-full rounded-full bg-emerald-400'
                                    }
                                    style={{
                                      width: `${Math.max(14, Math.min(100, (cohort.activeInstallCount / maxInstallCount) * 100))}%`,
                                    }}
                                  />
                                </div>
                                <p className="mt-3 text-xs text-muted-foreground">
                                  Last seen {formatDate(cohort.lastSeenAt)}
                                </p>
                              </button>
                            )
                          })}
                        </div>
                      ) : null}

                      <div className="overflow-hidden rounded-xl border border-border/70">
                        <table className="min-w-full divide-y divide-border/70 text-sm">
                          <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
                            <tr>
                              <th className="px-4 py-3">Device</th>
                              <th className="px-4 py-3">Criticality</th>
                              <th className="px-4 py-3">Open vulns</th>
                              <th className="px-4 py-3">Last seen</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-border/60 bg-background">
                            {devices.items.length === 0 ? (
                              <tr>
                                <td
                                  className="px-4 py-6 text-sm text-muted-foreground"
                                  colSpan={4}
                                >
                                  No devices found for this cohort.
                                </td>
                              </tr>
                            ) : (
                              devices.items.map((device) => (
                                <tr key={device.deviceAssetId} className="align-top">
                                  <td className="px-4 py-3 font-medium">
                                    <div>
                                      {device.deviceName}
                                      <p className="mt-1 text-xs text-muted-foreground">
                                        {formatVersion(device.version)}
                                      </p>
                                    </div>
                                  </td>
                                  <td className="px-4 py-3">
                                    <span
                                      className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(device.criticality))}`}
                                    >
                                      {device.criticality}
                                    </span>
                                  </td>
                                  <td className="px-4 py-3 text-muted-foreground">
                                    {device.openVulnerabilityCount}
                                  </td>
                                  <td className="px-4 py-3 text-muted-foreground">
                                    {formatDate(device.lastSeenAt)}
                                  </td>
                                </tr>
                              ))
                            )}
                          </tbody>
                        </table>
                      </div>
                      {devices.totalCount > devices.pageSize ? (
                        <div className="mt-3 flex items-center justify-between gap-3">
                          <p className="text-sm text-muted-foreground">
                            Page {devices.page} of{' '}
                            {Math.ceil(devices.totalCount / devices.pageSize)}
                          </p>
                          <div className="flex gap-2">
                            <Button
                              variant="outline"
                              size="sm"
                              disabled={devices.page <= 1}
                              onClick={() => onDevicePageChange(devices.page - 1)}
                            >
                              Previous
                            </Button>
                            <Button
                              variant="outline"
                              size="sm"
                              disabled={
                                devices.page >=
                                Math.ceil(devices.totalCount / devices.pageSize)
                              }
                              onClick={() => onDevicePageChange(devices.page + 1)}
                            >
                              Next
                            </Button>
                          </div>
                        </div>
                      ) : null}
                    </>
                  )
                })()}
              </section>
            ) : (
              <section className="rounded-2xl border border-border/70 bg-background/40 p-5 text-sm text-muted-foreground">
                No affected devices are linked to this approval task.
              </section>
            )}
          </TabsContent>

          <TabsContent value="timeline" className="pt-1">
            <section className="rounded-2xl border border-border/70 bg-background/40 p-5">
              <h2 className="mb-4 text-xs uppercase tracking-[0.14em] text-muted-foreground">
                Approval history
              </h2>
              <AuditTimeline
                events={auditEvents}
                emptyMessage="No timeline events recorded yet."
              />
            </section>
          </TabsContent>
        </Tabs>
      </section>
      </section>
      <aside className="space-y-5">
        {/* Sidebar cards added in later tasks */}
      </aside>
    </div>
  )
}

function buildApprovalTimelineTitle(action: string, userDisplayName: string | null) {
  const actor = userDisplayName ?? 'System'

  switch (action) {
    case 'Approved':
      return `${actor} approved this remediation decision.`
    case 'Denied':
      return `${actor} denied this remediation decision.`
    case 'AutoDenied':
      return `This approval request expired before anyone responded.`
    case 'Expired':
      return `${actor} closed or expired this remediation decision.`
    case 'Read':
      return `${actor} marked this approval item as read.`
    case 'Created':
      return `${actor} created this remediation decision.`
    default:
      return `${actor} updated this remediation decision.`
  }
}
