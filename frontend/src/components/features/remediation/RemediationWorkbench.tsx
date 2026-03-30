import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type { PagedDecisionList } from '@/api/remediation.schemas'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatDate, startCase } from '@/lib/formatting'
import { toneBadge } from '@/lib/tone-classes'
import { OpenEpisodeSparkline } from './OpenEpisodeSparkline'
import {
  approvalStatusLabel,
  approvalStatusTone,
  outcomeLabel,
  outcomeTone,
  riskBandTone,
  severityTone,
} from './remediation-utils'

type Filters = {
  search: string
  criticality: string
  outcome: string
  approvalStatus: string
  decisionState: string
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
  const quickFilterLabel = filters.approvalStatus === 'PendingApproval'
    ? 'Pending approval'
    : filters.decisionState === 'WithDecision'
      ? 'With decision'
      : filters.decisionState === 'NoDecision'
        ? 'No decision'
        : null

  return (
    <section className="space-y-5">
      <header className="rounded-[28px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_45%),var(--color-card)] p-5">
        <div className="grid gap-5 xl:grid-cols-[minmax(0,1.8fr)_minmax(360px,1fr)] xl:items-start">
          <div className="space-y-3">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Remediation workbench
            </p>
            <div className="space-y-1.5">
              <h1 className="text-3xl font-semibold tracking-[-0.04em]">
                Software remediation decisions
              </h1>
              <p className="max-w-3xl text-sm text-muted-foreground">
                Review the remediation posture across tracked software and move
                items that still need ownership, approval, or execution.
              </p>
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <Metric
              label="Software in scope"
              value={String(data.summary.softwareInScope)}
              active={!filters.decisionState && !filters.approvalStatus}
              onClick={() =>
                onFiltersChange({
                  ...filters,
                  decisionState: "",
                  approvalStatus: "",
                })
              }
            />
            <Metric
              label="With decision"
              value={String(data.summary.withDecision)}
              active={filters.decisionState === "WithDecision"}
              onClick={() =>
                onFiltersChange({
                  ...filters,
                  decisionState:
                    filters.decisionState === "WithDecision"
                      ? ""
                      : "WithDecision",
                  approvalStatus: "",
                })
              }
            />
            <Metric
              label="Pending approval"
              value={String(data.summary.pendingApproval)}
              active={filters.approvalStatus === "PendingApproval"}
              onClick={() =>
                onFiltersChange({
                  ...filters,
                  approvalStatus:
                    filters.approvalStatus === "PendingApproval"
                      ? ""
                      : "PendingApproval",
                  decisionState: "",
                })
              }
            />
            <Metric
              label="No decision"
              value={String(data.summary.noDecision)}
              active={filters.decisionState === "NoDecision"}
              onClick={() =>
                onFiltersChange({
                  ...filters,
                  decisionState:
                    filters.decisionState === "NoDecision" ? "" : "NoDecision",
                  approvalStatus: "",
                })
              }
            />
          </div>
        </div>
      </header>

      <section className="rounded-2xl border border-border/70 bg-card p-5">
        <div className="grid gap-3 lg:grid-cols-[minmax(0,1.6fr)_repeat(3,minmax(180px,1fr))]">
          <LabeledFilter label="Search software">
            <Input
              placeholder="Search software name"
              value={filters.search}
              onChange={(event) =>
                onFiltersChange({ ...filters, search: event.target.value })
              }
            />
          </LabeledFilter>

          <LabeledFilter label="Criticality">
            <Select
              value={filters.criticality || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                onFiltersChange({
                  ...filters,
                  criticality: nextValue !== "all" ? nextValue : "",
                });
              }}
            >
              <SelectTrigger>
                <SelectValue placeholder="All criticalities" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All criticalities</SelectItem>
                <SelectItem value="Critical">Critical</SelectItem>
                <SelectItem value="High">High</SelectItem>
                <SelectItem value="Medium">Medium</SelectItem>
                <SelectItem value="Low">Low</SelectItem>
              </SelectContent>
            </Select>
          </LabeledFilter>

          <LabeledFilter label="Decision">
            <Select
              value={filters.outcome || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                onFiltersChange({
                  ...filters,
                  outcome: nextValue !== "all" ? nextValue : "",
                });
              }}
            >
              <SelectTrigger>
                <SelectValue placeholder="All decisions" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All decisions</SelectItem>
                <SelectItem value="ApprovedForPatching">
                  {outcomeLabel("ApprovedForPatching")}
                </SelectItem>
                <SelectItem value="RiskAcceptance">
                  {outcomeLabel("RiskAcceptance")}
                </SelectItem>
                <SelectItem value="AlternateMitigation">
                  {outcomeLabel("AlternateMitigation")}
                </SelectItem>
                <SelectItem value="PatchingDeferred">
                  {outcomeLabel("PatchingDeferred")}
                </SelectItem>
              </SelectContent>
            </Select>
          </LabeledFilter>

          <LabeledFilter label="Approval status">
            <Select
              value={filters.approvalStatus || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                onFiltersChange({
                  ...filters,
                  approvalStatus: nextValue !== "all" ? nextValue : "",
                });
              }}
            >
              <SelectTrigger>
                <SelectValue placeholder="All approval states" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All approval states</SelectItem>
                <SelectItem value="PendingApproval">
                  Pending approval
                </SelectItem>
                <SelectItem value="Approved">Approved</SelectItem>
                <SelectItem value="Reopened">Reopened</SelectItem>
              </SelectContent>
            </Select>
          </LabeledFilter>
        </div>

        {quickFilterLabel ? (
          <div className="mt-4 flex flex-wrap items-center gap-2">
            <span className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
              Quick filter
            </span>
            <button
              type="button"
              onClick={() =>
                onFiltersChange({
                  ...filters,
                  decisionState: "",
                  approvalStatus: "",
                })
              }
              className="inline-flex items-center rounded-full border border-primary/30 bg-primary/10 px-3 py-1 text-xs font-medium text-foreground transition hover:border-primary/40 hover:bg-primary/15"
            >
              {quickFilterLabel}
              <span className="ml-2 text-muted-foreground">Clear</span>
            </button>
          </div>
        ) : null}

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
                  <td
                    className="px-4 py-6 text-sm text-muted-foreground"
                    colSpan={6}
                  >
                    No software matches the current filters.
                  </td>
                </tr>
              ) : (
                data.items.map((item) => (
                  <tr key={item.tenantSoftwareId} className="align-top">
                    <td className="px-4 py-3">
                      <div className="space-y-1">
                        {item.tenantSoftwareId ? (
                          <Link
                            to="/software/$id/remediation"
                            params={{ id: item.tenantSoftwareId }}
                            className="font-medium hover:text-primary"
                          >
                            {startCase(item.softwareName)}
                          </Link>
                        ) : (
                          <span className="font-medium">
                            {startCase(item.softwareName)}
                          </span>
                        )}
                        <div className="flex gap-1.5">
                          <span
                            className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(item.criticality))}`}
                          >
                            {item.criticality}
                          </span>
                          {item.tenantSoftwareId ? (
                            <Link
                              to="/software/$id"
                              params={{ id: item.tenantSoftwareId }}
                              search={{
                                page: 1,
                                pageSize: 25,
                                version: "",
                                tab: "overview",
                              }}
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
                          <span
                            className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(outcomeTone(item.outcome))}`}
                          >
                            {outcomeLabel(item.outcome)}
                          </span>
                          <span
                            className={`ml-1 inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(approvalStatusTone(item.approvalStatus!))}`}
                          >
                            {approvalStatusLabel(item.approvalStatus!)}
                          </span>
                          {item.decidedAt ? (
                            <p className="text-[10px] text-muted-foreground">
                              {formatDate(item.decidedAt)}
                            </p>
                          ) : null}
                        </div>
                      ) : (
                        <span className="text-xs text-muted-foreground">
                          No decision
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      <div className="text-sm">
                        {item.totalVulnerabilities} total
                      </div>
                      {item.criticalCount > 0 ? (
                        <div className="text-xs text-tone-danger-foreground">
                          {item.criticalCount} critical
                        </div>
                      ) : null}
                      {item.highCount > 0 ? (
                        <div className="text-xs text-tone-warning-foreground">
                          {item.highCount} high
                        </div>
                      ) : null}
                    </td>
                    <td className="px-4 py-3">
                      {item.riskBand ? (
                        <span
                          className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-semibold ${toneBadge(riskBandTone(item.riskBand))}`}
                        >
                          {item.riskBand} ({item.riskScore?.toFixed(0)})
                        </span>
                      ) : (
                        <span className="text-xs text-muted-foreground">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {item.slaStatus ? (
                        <div className="space-y-0.5">
                          <span
                            className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(slaTone(item.slaStatus))}`}
                          >
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
                    <td className="px-4 py-3">
                      <div className="space-y-1">
                        <div className="font-medium text-foreground">
                          {item.affectedDeviceCount}
                        </div>
                        <OpenEpisodeSparkline points={item.openEpisodeTrend} />
                      </div>
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
  );
}

function Metric({
  label,
  value,
  active = false,
  onClick,
}: {
  label: string
  value: string
  active?: boolean
  onClick?: () => void
}) {
  const content = (
    <div className={`rounded-2xl border px-4 py-3 transition ${active ? 'border-primary/40 bg-primary/10' : 'border-border/70 bg-background/70'}`}>
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-2xl font-semibold tracking-[-0.03em]">{value}</p>
    </div>
  )

  if (!onClick) {
    return content
  }

  return (
    <button type="button" onClick={onClick} className="text-left">
      {content}
    </button>
  )
}

function LabeledFilter({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="space-y-2">
      <span className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">{label}</span>
      {children}
    </label>
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
