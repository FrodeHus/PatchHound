import { Link } from '@tanstack/react-router'
import type { PagedDecisionList } from '@/api/remediation.schemas'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { toneBadge } from '@/lib/tone-classes'
import { formatDate } from '@/lib/formatting'
import {
  outcomeLabel,
  outcomeTone,
  approvalStatusTone,
  riskBandTone,
  severityTone,
} from './remediation-utils'

type Filters = {
  search: string
  criticality: string
  outcome: string
  approvalStatus: string
}

type Props = {
  data: PagedDecisionList
  filters: Filters
  onFiltersChange: (filters: Filters) => void
  onPageChange: (page: number) => void
}

export function RemediationWorkbench({
  data,
  filters,
  onFiltersChange,
  onPageChange,
}: Props) {
  const withDecision = data.items.filter((i) => i.outcome !== null)
  const pendingCount = data.items.filter((i) => i.approvalStatus === 'PendingApproval').length
  const noDecisionCount = data.items.filter((i) => i.outcome === null).length

  return (
    <section className="space-y-5">
      <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Remediation workbench</p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">Software remediation decisions</h1>
            <p className="max-w-3xl text-sm text-muted-foreground">
              Review remediation status across all software assets. Track decisions, approval status, risk scores, and SLA compliance.
            </p>
          </div>
          <div className="grid min-w-[220px] gap-3 rounded-xl border border-border/70 bg-background/50 p-4">
            <Metric label="Total assets" value={String(data.totalCount)} />
            <Metric label="With decision" value={String(withDecision.length)} />
            <Metric label="Pending approval" value={String(pendingCount)} />
            <Metric label="No decision" value={String(noDecisionCount)} />
          </div>
        </div>
      </header>

      <section className="rounded-2xl border border-border/70 bg-card p-5">
        <div className="grid gap-3 lg:grid-cols-[minmax(0,1.4fr)_repeat(3,minmax(0,1fr))]">
          <Input
            placeholder="Search software name"
            value={filters.search}
            onChange={(event) => onFiltersChange({ ...filters, search: event.target.value })}
          />
          <Select
            value={filters.criticality || '__all__'}
            onValueChange={(value) =>
              onFiltersChange({ ...filters, criticality: value && value !== '__all__' ? value : '' })
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Criticality" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">All criticalities</SelectItem>
              <SelectItem value="Critical">Critical</SelectItem>
              <SelectItem value="High">High</SelectItem>
              <SelectItem value="Medium">Medium</SelectItem>
              <SelectItem value="Low">Low</SelectItem>
            </SelectContent>
          </Select>
          <Select
            value={filters.outcome || '__all__'}
            onValueChange={(value) =>
              onFiltersChange({ ...filters, outcome: value && value !== '__all__' ? value : '' })
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Outcome" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">All outcomes</SelectItem>
              <SelectItem value="ApprovedForPatching">Approved for Patching</SelectItem>
              <SelectItem value="RiskAcceptance">Risk Acceptance</SelectItem>
              <SelectItem value="AlternateMitigation">Alternate Mitigation</SelectItem>
              <SelectItem value="PatchingDeferred">Patching Deferred</SelectItem>
            </SelectContent>
          </Select>
          <Select
            value={filters.approvalStatus || '__all__'}
            onValueChange={(value) =>
              onFiltersChange({ ...filters, approvalStatus: value && value !== '__all__' ? value : '' })
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Approval status" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">All statuses</SelectItem>
              <SelectItem value="PendingApproval">Pending Approval</SelectItem>
              <SelectItem value="Approved">Approved</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="mt-5 overflow-hidden rounded-xl border border-border/70">
          <table className="min-w-full divide-y divide-border/70 text-sm">
            <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
              <tr>
                <th className="px-4 py-3">Software</th>
                <th className="px-4 py-3">Decision</th>
                <th className="px-4 py-3">Vulnerabilities</th>
                <th className="px-4 py-3">Risk</th>
                <th className="px-4 py-3">SLA</th>
                <th className="px-4 py-3">Devices</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border/60 bg-background">
              {data.items.length === 0 ? (
                <tr>
                  <td className="px-4 py-6 text-sm text-muted-foreground" colSpan={6}>
                    No software assets match the current filters.
                  </td>
                </tr>
              ) : (
                data.items.map((item) => (
                  <tr key={item.assetId} className="align-top">
                    <td className="px-4 py-3">
                      <div className="space-y-1">
                        <Link
                          to="/assets/$id/remediation"
                          params={{ id: item.assetId }}
                          className="font-medium hover:text-primary"
                        >
                          {item.assetName}
                        </Link>
                        <div className="flex gap-1.5">
                          <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(item.criticality))}`}>
                            {item.criticality}
                          </span>
                          {item.tenantSoftwareId ? (
                            <Link
                              to="/software/$id"
                              params={{ id: item.tenantSoftwareId }}
                              search={{ page: 1, pageSize: 25, version: '' }}
                              className="text-[10px] text-muted-foreground hover:text-primary"
                            >
                              Software detail
                            </Link>
                          ) : null}
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      {item.outcome ? (
                        <div className="space-y-1">
                          <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(outcomeTone(item.outcome))}`}>
                            {outcomeLabel(item.outcome)}
                          </span>
                          <span className={`ml-1 inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(approvalStatusTone(item.approvalStatus!))}`}>
                            {item.approvalStatus}
                          </span>
                          {item.decidedAt ? (
                            <p className="text-[10px] text-muted-foreground">
                              {formatDate(item.decidedAt)}
                            </p>
                          ) : null}
                        </div>
                      ) : (
                        <span className="text-xs text-muted-foreground">No decision</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      <div className="text-sm">{item.totalVulnerabilities} total</div>
                      {item.criticalCount > 0 ? (
                        <div className="text-xs text-tone-danger-foreground">{item.criticalCount} critical</div>
                      ) : null}
                      {item.highCount > 0 ? (
                        <div className="text-xs text-tone-warning-foreground">{item.highCount} high</div>
                      ) : null}
                    </td>
                    <td className="px-4 py-3">
                      {item.riskBand ? (
                        <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-semibold ${toneBadge(riskBandTone(item.riskBand))}`}>
                          {item.riskBand} ({item.riskScore?.toFixed(0)})
                        </span>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {item.slaStatus ? (
                        <div className="space-y-0.5">
                          <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(slaTone(item.slaStatus))}`}>
                            {item.slaStatus}
                          </span>
                          {item.slaDueDate ? (
                            <p className="text-[10px] text-muted-foreground">
                              Due {formatDate(item.slaDueDate)}
                            </p>
                          ) : null}
                        </div>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {item.affectedDeviceCount}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <div className="mt-4 flex items-center justify-between gap-3">
          <p className="text-sm text-muted-foreground">
            Page {data.page} of {Math.max(data.totalPages, 1)}
          </p>
          <div className="flex gap-2">
            <Button
              variant="outline"
              disabled={data.page <= 1}
              onClick={() => onPageChange(data.page - 1)}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              disabled={data.page >= Math.max(data.totalPages, 1)}
              onClick={() => onPageChange(data.page + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      </section>
    </section>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-sm font-medium">{value}</p>
    </div>
  )
}

function slaTone(status: string) {
  switch (status) {
    case 'OnTrack': return 'success' as const
    case 'AtRisk': return 'warning' as const
    case 'Breached': return 'danger' as const
    default: return 'neutral' as const
  }
}
