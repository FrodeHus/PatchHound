import { useState } from 'react'
import type { ApprovalTaskDetail as ApprovalTaskDetailType } from '@/api/approval-tasks.schemas'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { toneBadge } from '@/lib/tone-classes'
import { formatDate } from '@/lib/formatting'
import { ApprovalTypeBadge, ApprovalStatusBadge } from './ApprovalBadge'
import { ApprovalExpiryCountdown } from './ApprovalExpiryCountdown'
import {
  CheckCircle,
  XCircle,
  Clock,
  Eye,
  AlertTriangle,
  ShieldAlert,
  MessageSquare,
} from 'lucide-react'

type Props = {
  data: ApprovalTaskDetailType
  onResolve: (action: 'approve' | 'deny', justification?: string) => void
  onMarkRead: () => void
  onVulnPageChange: (page: number) => void
  onDevicePageChange: (page: number) => void
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

function auditActionIcon(action: string) {
  switch (action) {
    case 'Approved':
      return <CheckCircle className="size-4 text-tone-success-foreground" />
    case 'Denied':
      return <XCircle className="size-4 text-tone-danger-foreground" />
    case 'AutoDenied':
      return <Clock className="size-4 text-muted-foreground" />
    case 'Created':
      return <ShieldAlert className="size-4 text-primary" />
    default:
      return <Clock className="size-4 text-muted-foreground" />
  }
}

export function ApprovalTaskDetail({
  data,
  onResolve,
  onMarkRead,
  onVulnPageChange,
  onDevicePageChange,
}: Props) {
  const [justification, setJustification] = useState('')
  const [resolveAction, setResolveAction] = useState<'approve' | 'deny' | null>(
    null
  )
  const isPending = data.status === 'Pending'
  const justificationRequired = data.requiresJustification

  function handleResolve(action: 'approve' | 'deny') {
    if (justificationRequired && !justification.trim()) {
      setResolveAction(action)
      return
    }
    onResolve(action, justification.trim() || undefined)
  }

  return (
    <section className="space-y-5">
      {/* Software overview header */}
      <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Approval task
            </p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">
              {data.assetName}
            </h1>
            <div className="flex flex-wrap items-center gap-2">
              <span
                className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(data.criticality))}`}
              >
                {data.criticality}
              </span>
              <ApprovalTypeBadge type={data.type} />
              <ApprovalStatusBadge status={data.status} />
            </div>
            <p className="text-sm text-muted-foreground">
              Decision by {data.decidedByName}
            </p>
          </div>
          <div className="grid min-w-[220px] gap-3 rounded-xl border border-border/70 bg-background/50 p-4">
            <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
              <ApprovalExpiryCountdown expiresAt={data.expiresAt} />
            </div>
            {data.riskBand ? (
              <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
                <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                  Risk
                </p>
                <p className="mt-2">
                  <span
                    className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-semibold ${toneBadge(riskBandTone(data.riskBand))}`}
                  >
                    {data.riskBand} ({data.riskScore?.toFixed(0)})
                  </span>
                </p>
              </div>
            ) : null}
            {data.slaStatus ? (
              <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
                <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
                  SLA
                </p>
                <p className="mt-2 text-sm">
                  {data.slaStatus}
                  {data.slaDueDate ? (
                    <span className="ml-1 text-muted-foreground">
                      (due {formatDate(data.slaDueDate)})
                    </span>
                  ) : null}
                </p>
              </div>
            ) : null}
          </div>
        </div>
      </header>

      {/* Decision justification */}
      {data.justification ? (
        <section className="rounded-2xl border border-border/70 bg-card p-5">
          <h2 className="mb-3 text-xs uppercase tracking-[0.14em] text-muted-foreground">
            Decision justification
          </h2>
          <p className="text-sm leading-relaxed">{data.justification}</p>
        </section>
      ) : null}

      {/* Action section */}
      {isPending ? (
        <section className="rounded-2xl border border-primary/20 bg-card p-5">
          <h2 className="mb-4 text-xs uppercase tracking-[0.14em] text-muted-foreground">
            Your decision
          </h2>
          <div className="space-y-4">
            <Textarea
              placeholder={
                justificationRequired
                  ? 'Justification required for both approve and deny'
                  : 'Optional justification'
              }
              value={justification}
              onChange={(e) => setJustification(e.target.value)}
              rows={3}
            />
            {resolveAction && justificationRequired && !justification.trim() ? (
              <p className="flex items-center gap-1.5 text-sm text-tone-danger-foreground">
                <AlertTriangle className="size-3.5" />
                Justification is required to {resolveAction} this task.
              </p>
            ) : null}
            <div className="flex gap-3">
              <Button onClick={() => handleResolve('approve')}>
                <CheckCircle className="mr-1.5 size-4" />
                Approve
              </Button>
              <Button variant="destructive" onClick={() => handleResolve('deny')}>
                <XCircle className="mr-1.5 size-4" />
                Deny
              </Button>
            </div>
          </div>
        </section>
      ) : !data.readAt ? (
        <section className="rounded-2xl border border-border/70 bg-card p-5">
          <Button variant="outline" onClick={onMarkRead}>
            <Eye className="mr-1.5 size-4" />
            Mark as read
          </Button>
        </section>
      ) : null}

      {/* Vulnerability table */}
      <section className="rounded-2xl border border-border/70 bg-card p-5">
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
                    data.vulnerabilities.totalCount /
                      data.vulnerabilities.pageSize
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

      {/* Device table (only for patching-type tasks) */}
      {data.devices ? (
        <section className="rounded-2xl border border-border/70 bg-card p-5">
          <h2 className="mb-4 text-xs uppercase tracking-[0.14em] text-muted-foreground">
            Affected devices ({data.devices.totalCount})
          </h2>
          <div className="overflow-hidden rounded-xl border border-border/70">
            <table className="min-w-full divide-y divide-border/70 text-sm">
              <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
                <tr>
                  <th className="px-4 py-3">Device</th>
                  <th className="px-4 py-3">Criticality</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60 bg-background">
                {data.devices.items.length === 0 ? (
                  <tr>
                    <td
                      className="px-4 py-6 text-sm text-muted-foreground"
                      colSpan={2}
                    >
                      No devices found.
                    </td>
                  </tr>
                ) : (
                  data.devices.items.map((device) => (
                    <tr key={device.deviceAssetId} className="align-top">
                      <td className="px-4 py-3 font-medium">
                        {device.deviceName}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(device.criticality))}`}
                        >
                          {device.criticality}
                        </span>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
          {data.devices.totalCount > data.devices.pageSize ? (
            <div className="mt-3 flex items-center justify-between gap-3">
              <p className="text-sm text-muted-foreground">
                Page {data.devices.page} of{' '}
                {Math.ceil(
                  data.devices.totalCount / data.devices.pageSize
                )}
              </p>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={data.devices.page <= 1}
                  onClick={() => onDevicePageChange(data.devices!.page - 1)}
                >
                  Previous
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={
                    data.devices.page >=
                    Math.ceil(
                      data.devices.totalCount / data.devices.pageSize
                    )
                  }
                  onClick={() => onDevicePageChange(data.devices!.page + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          ) : null}
        </section>
      ) : null}

      {/* Analyst recommendations */}
      {data.recommendations.length > 0 ? (
        <section className="rounded-2xl border border-border/70 bg-card p-5">
          <h2 className="mb-4 text-xs uppercase tracking-[0.14em] text-muted-foreground">
            Analyst recommendations
          </h2>
          <div className="space-y-3">
            {data.recommendations.map((rec) => (
              <div
                key={rec.id}
                className="rounded-xl border border-border/60 bg-background/50 p-4"
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <MessageSquare className="size-4 text-muted-foreground" />
                    <span className="text-xs font-medium">
                      {rec.recommendedOutcome}
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

      {/* Audit trail */}
      {data.auditTrail.length > 0 ? (
        <section className="rounded-2xl border border-border/70 bg-card p-5">
          <h2 className="mb-4 text-xs uppercase tracking-[0.14em] text-muted-foreground">
            Approval history
          </h2>
          <div className="space-y-0">
            {data.auditTrail.map((entry, i) => (
              <div
                key={`${entry.timestamp}-${i}`}
                className="flex gap-3 border-l-2 border-border/60 py-3 pl-4"
              >
                <div className="mt-0.5">{auditActionIcon(entry.action)}</div>
                <div className="min-w-0 flex-1 space-y-0.5">
                  <p className="text-sm">
                    <span className="font-medium">
                      {entry.userDisplayName ?? 'System'}
                    </span>{' '}
                    <span className="text-muted-foreground">
                      {entry.action.toLowerCase()}
                    </span>
                  </p>
                  {entry.justification ? (
                    <p className="text-sm italic text-muted-foreground">
                      &ldquo;{entry.justification}&rdquo;
                    </p>
                  ) : null}
                  <p className="text-[10px] text-muted-foreground">
                    {formatDate(entry.timestamp)}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </section>
      ) : null}
    </section>
  )
}
