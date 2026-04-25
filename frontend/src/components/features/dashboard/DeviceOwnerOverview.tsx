import { Link, useNavigate } from '@tanstack/react-router'
import { KeyRound } from "lucide-react";
import { useState } from 'react'
import type { OwnerAction, OwnerAssetSummary, OwnerCloudAppAction, OwnerDashboardSummary } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

// Phase 1 canonical cleanup (Task 15): the dashboard summary schema is
// still on the legacy asset-shaped contract (assetId/assetName/etc). The
// component has been renamed to DeviceOwnerOverview but continues to
// read from the existing schema until Phase 5 rewires the dashboard API
// to the canonical Device identity.

type Props = {
  summary: OwnerDashboardSummary
  isLoading: boolean
}

function formatDueDate(value: string | null) {
  if (!value) return 'No due date'
  return new Date(value).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' })
}

function summarizeSoftware(names: string[]) {
  if (names.length === 0) return 'Affected software needs review'
  if (names.length === 1) return names[0]
  if (names.length === 2) return `${names[0]} and ${names[1]}`
  return `${names[0]}, ${names[1]}, and ${names.length - 2} more`
}

function buildActionHeadline(softwareName: string, softwareNames: string[]) {
  if (softwareNames.length === 0) {
    return `Review remediation needed for ${softwareName}`
  }

  return `Review ${summarizeSoftware(softwareNames)} on ${softwareName}`
}

function formatLastSeen(value: string | null | undefined) {
  if (!value) return null
  const d = new Date(value)
  const diff = Date.now() - d.getTime()
  const days = Math.floor(diff / 86_400_000)
  if (days === 0) return 'Seen today'
  if (days === 1) return 'Seen yesterday'
  return `Seen ${days}d ago`
}

function SeverityBar({ critical, high, medium, low }: { critical: number; high: number; medium: number; low: number }) {
  const total = critical + high + medium + low
  if (total === 0) return null
  return (
    <div className="mt-2 flex h-1.5 overflow-hidden rounded-full bg-muted">
      {critical > 0 && <div className="bg-destructive" style={{ width: `${(critical / total) * 100}%` }} />}
      {high > 0 && <div className="bg-orange-500" style={{ width: `${(high / total) * 100}%` }} />}
      {medium > 0 && <div className="bg-amber-500" style={{ width: `${(medium / total) * 100}%` }} />}
      {low > 0 && <div className="bg-muted-foreground/40" style={{ width: `${(low / total) * 100}%` }} />}
    </div>
  )
}

function actionStateTone(value: string) {
  switch (value) {
    case 'InProgress':
      return 'bg-blue-50 text-blue-700 border-blue-200'
    case 'Pending':
      return 'bg-amber-50 text-amber-700 border-amber-200'
    case 'AwaitingDecision':
      return 'bg-orange-50 text-orange-700 border-orange-200'
    default:
      return 'bg-muted text-muted-foreground border-border'
  }
}

function actionStateLabel(value: string) {
  if (value === 'AwaitingDecision') return 'Decision needed'
  return value
}

function ownerAssignmentTone(value: string) {
  switch (value) {
    case 'Rule':
      return 'border-emerald-500/25 bg-emerald-500/10 text-emerald-700'
    case 'Manual':
      return 'border-sky-500/25 bg-sky-500/10 text-sky-700'
    case 'Default':
      return 'border-border/70 bg-muted/50 text-muted-foreground'
    default:
      return 'border-amber-500/25 bg-amber-500/10 text-amber-700'
  }
}

function formatOwnerRoutingDetail(ownerTeamName: string, ownerAssignmentSource: string) {
  switch (ownerAssignmentSource) {
    case 'Rule':
      return `Rule managed by ${ownerTeamName}`
    case 'Manual':
      return `Manually assigned to ${ownerTeamName}`
    case 'Default':
      return `Defaulted to ${ownerTeamName}`
    default:
      return ownerTeamName
  }
}

type ActionSort = 'urgency' | 'severity' | 'team'
type ActionFilter = 'all' | 'overdue'
type DeviceSort = 'risk' | 'name' | 'criticality'

type CombinedAction =
  | { kind: 'remediation'; item: OwnerAction }
  | { kind: 'cloudApp'; item: OwnerCloudAppAction }

function combinedIsOverdue(a: CombinedAction): boolean {
  if (a.kind === 'remediation') return !!a.item.dueDate && new Date(a.item.dueDate) < new Date()
  return a.item.expiredCredentialCount > 0
}

function combinedDueDate(a: CombinedAction): string | null {
  if (a.kind === 'remediation') return a.item.dueDate
  return a.item.nearestExpiryAt
}

function combinedTeamName(a: CombinedAction): string {
  return a.kind === 'remediation' ? a.item.ownerTeamName : a.item.ownerTeamName
}

function sortCombined(items: CombinedAction[], sort: ActionSort): CombinedAction[] {
  const severityRank = (s: string) => ({ Critical: 4, High: 3, Medium: 2, Low: 1 })[s] ?? 0
  return [...items].sort((a, b) => {
    if (sort === 'urgency') {
      const aOverdue = combinedIsOverdue(a)
      const bOverdue = combinedIsOverdue(b)
      if (aOverdue !== bOverdue) return aOverdue ? -1 : 1
      const aDate = combinedDueDate(a)
      const bDate = combinedDueDate(b)
      if (aDate && bDate) return new Date(aDate).getTime() - new Date(bDate).getTime()
      if (aDate) return -1
      if (bDate) return 1
      if (a.kind === 'remediation' && b.kind === 'remediation')
        return severityRank(b.item.severity) - severityRank(a.item.severity)
      return 0
    }
    if (sort === 'severity') {
      if (a.kind === 'remediation' && b.kind === 'remediation')
        return severityRank(b.item.severity) - severityRank(a.item.severity)
      if (a.kind === 'remediation') return -1
      if (b.kind === 'remediation') return 1
      return 0
    }
    if (sort === 'team') return combinedTeamName(a).localeCompare(combinedTeamName(b))
    return 0
  })
}

function sortDevices(items: OwnerAssetSummary[], sort: DeviceSort): OwnerAssetSummary[] {
  const critRank = (c: string) => ({ Critical: 4, High: 3, Medium: 2, Low: 1 })[c] ?? 0
  return [...items].sort((a, b) => {
    if (sort === 'name') return a.assetName.localeCompare(b.assetName)
    if (sort === 'criticality') return critRank(b.criticality) - critRank(a.criticality)
    return (b.currentRiskScore ?? 0) - (a.currentRiskScore ?? 0)
  })
}

export function DeviceOwnerOverview({ summary, isLoading }: Props) {
  const [actionSort, setActionSort] = useState<ActionSort>('urgency')
  const [actionFilter, setActionFilter] = useState<ActionFilter>('all')
  const [deviceSort, setDeviceSort] = useState<DeviceSort>('risk')
  const navigate = useNavigate()

  const allCombined: CombinedAction[] = [
    ...summary.actions.map(item => ({ kind: 'remediation' as const, item })),
    ...(summary.cloudAppActions ?? []).map(item => ({ kind: 'cloudApp' as const, item })),
  ]

  const visibleActions = sortCombined(
    actionFilter === 'overdue'
      ? allCombined.filter(combinedIsOverdue)
      : allCombined,
    actionSort
  )

  const visibleDevices = sortDevices(summary.topOwnedAssets, deviceSort)

  return (
    <section className="space-y-6 pb-4">
      <Card className="overflow-hidden rounded-[2rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_12%,var(--background)),var(--card)_55%,var(--background))] shadow-[0_28px_70px_-48px_rgba(0,0,0,0.55)]">
        <CardContent className="p-6 sm:p-8">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.5fr)_minmax(20rem,1fr)]">
            <div className="space-y-4">
              <Badge
                variant="outline"
                className="rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-primary"
              >
                Device owner view
              </Badge>
              <div>
                <h1 className="text-3xl font-semibold tracking-[-0.06em] sm:text-4xl">
                  What needs your attention on the devices you own
                </h1>
                <p className="mt-3 max-w-3xl text-base leading-7 text-muted-foreground">
                  This view focuses only on the devices you are responsible for.
                  It is written to answer three questions quickly: which
                  software on your devices needs attention, what matters most,
                  and what you need to do next.
                </p>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>Action list</CardTitle>
                <CardDescription className="mt-1">
                  The software on devices you own that needs follow-through.
                </CardDescription>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <div className="flex rounded-lg border border-border/70 text-[11px]">
                  {(["all", "overdue"] as ActionFilter[]).map((f) => (
                    <button
                      key={f}
                      onClick={() => setActionFilter(f)}
                      className={`px-3 py-1.5 capitalize first:rounded-l-lg last:rounded-r-lg transition ${actionFilter === f ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}
                    >
                      {f === "all" ? "All" : "Overdue"}
                    </button>
                  ))}
                </div>
                <div className="flex rounded-lg border border-border/70 text-[11px]">
                  {(["urgency", "severity", "team"] as ActionSort[]).map(
                    (s) => (
                      <button
                        key={s}
                        onClick={() => setActionSort(s)}
                        className={`px-3 py-1.5 capitalize first:rounded-l-lg last:rounded-r-lg transition ${actionSort === s ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}
                      >
                        {s}
                      </button>
                    ),
                  )}
                </div>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {isLoading ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                Loading your action list…
              </div>
            ) : visibleActions.length === 0 ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                {actionFilter === "overdue"
                  ? "No overdue actions."
                  : "No open remediation or credential actions are assigned to your teams right now."}
              </div>
            ) : (
              visibleActions.map((combined) => {
                const isOverdue = combinedIsOverdue(combined);
                if (combined.kind === "cloudApp") {
                  const app = combined.item;
                  return (
                    <button
                      key={`cloudapp-${app.cloudApplicationId}`}
                      type="button"
                      onClick={() =>
                        void (navigate as unknown as (o: { to: string; params: Record<string, string> }) => Promise<void>)({
                          to: '/assets/applications/$id',
                          params: { id: app.cloudApplicationId },
                        })
                      }
                      className={`w-full text-left rounded-[1.2rem] border px-4 py-4 transition hover:-translate-y-0.5 hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${isOverdue ? "border-destructive/30 bg-destructive/5" : "border-amber-500/30 bg-amber-500/5"}`}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="flex items-center gap-1 rounded-full border border-amber-500/40 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700">
                              <KeyRound className="size-3" />
                              Credential expiry
                            </span>
                            <Badge
                              variant="outline"
                              className="rounded-full px-2 py-0.5 text-[11px]"
                            >
                              {app.ownerTeamName}
                            </Badge>
                            <Badge
                              variant="outline"
                              className={`rounded-full px-2 py-0.5 text-[11px] ${ownerAssignmentTone(app.ownerAssignmentSource)}`}
                            >
                              {app.ownerAssignmentSource}
                            </Badge>
                            {app.expiredCredentialCount > 0 && (
                              <span className="rounded-full border border-destructive/30 bg-destructive/8 px-2 py-0.5 text-[11px] font-medium text-destructive">
                                {app.expiredCredentialCount} expired
                              </span>
                            )}
                            {app.expiringCredentialCount > 0 && (
                              <span className="rounded-full border border-amber-500/40 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700">
                                {app.expiringCredentialCount} expiring soon
                              </span>
                            )}
                          </div>
                          <div className="mt-2 text-base font-medium tracking-tight">
                            {app.appName}
                          </div>
                          {app.appId && (
                            <div className="mt-0.5 font-mono text-xs text-muted-foreground">
                              {app.appId}
                            </div>
                          )}
                          <div className="mt-1 text-sm text-muted-foreground">
                            {app.expiredCredentialCount > 0
                              ? "This application has expired credentials that need to be rotated."
                              : "This application has credentials expiring within the next 7 days."}
                          </div>
                          <div className="mt-1 text-xs text-muted-foreground">
                            {formatOwnerRoutingDetail(app.ownerTeamName, app.ownerAssignmentSource)}
                          </div>
                        </div>
                        <div className="text-right text-sm text-muted-foreground shrink-0">
                          {app.nearestExpiryAt && (
                            <div
                              className={
                                isOverdue
                                  ? "font-medium text-destructive"
                                  : "font-medium text-amber-600"
                              }
                            >
                              {isOverdue ? "Expired" : "Expires"}{" "}
                              {formatDueDate(app.nearestExpiryAt)}
                            </div>
                          )}
                          <div className="mt-1">
                            Assigned to {app.ownerTeamName}
                          </div>
                        </div>
                      </div>
                    </button>
                  );
                }

                const item = combined.item;
                return (
                  <div
                    key={`${item.tenantSoftwareId}-${item.vulnerabilityId}`}
                    className={`rounded-[1.2rem] border px-4 py-4 ${isOverdue ? "border-destructive/30 bg-destructive/5" : "border-border/60 bg-background/35"}`}
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <Badge
                            variant="outline"
                            className="rounded-full px-2 py-0.5 text-[11px]"
                          >
                            {item.severity}
                          </Badge>
                          <Badge
                            variant="outline"
                            className="rounded-full px-2 py-0.5 text-[11px]"
                          >
                            {item.ownerTeamName}
                          </Badge>
                          <span
                            className={`rounded-full border px-2 py-0.5 text-[11px] font-medium ${actionStateTone(item.actionState)}`}
                          >
                            {actionStateLabel(item.actionState)}
                          </span>
                          {item.episodeRiskBand ? (
                            <span className="text-xs text-muted-foreground">
                              {item.episodeRiskBand} risk
                            </span>
                          ) : null}
                        </div>
                        <div className="mt-2 text-base font-medium tracking-tight">
                          {buildActionHeadline(
                            item.softwareName,
                            item.softwareNames,
                          )}
                        </div>
                        <div className="mt-1 text-sm text-muted-foreground">
                          {item.ownerSummary}
                        </div>
                        {item.softwareNames.length > 0 ? (
                          <div className="mt-2 text-sm text-muted-foreground">
                            Software covered by this remediation:{" "}
                            {item.softwareNames.join(", ")}
                          </div>
                        ) : null}
                        <div className="mt-2 text-xs text-muted-foreground">
                          Technical reference: {item.externalId}
                        </div>
                      </div>
                      <div className="text-right text-sm text-muted-foreground">
                        <div>Due {formatDueDate(item.dueDate)}</div>
                        <div className="mt-1">
                          Assigned to {item.ownerTeamName}
                        </div>
                      </div>
                    </div>
                    <div className="mt-3 flex gap-2">
                      {item.taskId ? (
                        <Button
                          size="sm"
                          render={
                            <Link
                              to="/remediation/task/$id"
                              params={{ id: item.taskId }}
                            />
                          }
                        >
                          Open remediation task
                        </Button>
                      ) : (
                        <Button
                          size="sm"
                          render={
                            <Link
                              to="/remediation/cases/$caseId"
                              params={{ caseId: item.tenantSoftwareId }}
                            />
                          }
                        >
                          Open remediation case
                        </Button>
                      )}
                      <Button
                        size="sm"
                        variant="outline"
                        render={
                          <Link
                            to="/vulnerabilities/$id"
                            params={{ id: item.vulnerabilityId }}
                          />
                        }
                      >
                        Vulnerability detail
                      </Button>
                    </div>
                  </div>
                );
              })
            )}
          </CardContent>
        </Card>

        <Card
          id="owned-devices-needing-attention"
          className="rounded-[1.6rem] border-border/70"
        >
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>Devices under the most pressure</CardTitle>
                <CardDescription className="mt-1">
                  Your owned devices with the highest current risk.
                </CardDescription>
              </div>
              <div className="flex rounded-lg border border-border/70 text-[11px]">
                {(["risk", "name", "criticality"] as DeviceSort[]).map((s) => (
                  <button
                    key={s}
                    onClick={() => setDeviceSort(s)}
                    className={`px-3 py-1.5 capitalize first:rounded-l-lg last:rounded-r-lg transition ${deviceSort === s ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}
                  >
                    {s}
                  </button>
                ))}
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {visibleDevices.map((item) => (
              <div
                key={item.assetId}
                className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="font-medium tracking-tight">
                      {item.assetName}
                    </div>
                    <div className="mt-2 flex flex-wrap items-center gap-2">
                      <Badge
                        variant="outline"
                        className="rounded-full px-2 py-0.5 text-[11px]"
                      >
                        {item.criticality}
                      </Badge>
                      <Badge
                        variant="outline"
                        className="rounded-full px-2 py-0.5 text-[11px]"
                      >
                        {item.deviceGroupName || "Ungrouped"}
                      </Badge>
                      {item.riskBand ? (
                        <Badge
                          variant="outline"
                          className="rounded-full px-2 py-0.5 text-[11px]"
                        >
                          {item.riskBand}
                        </Badge>
                      ) : null}
                    </div>
                    <div className="mt-2 text-sm text-muted-foreground">
                      {item.openEpisodeCount} open exposure items
                      {formatLastSeen(item.lastSeenAt)
                        ? ` · ${formatLastSeen(item.lastSeenAt)}`
                        : ""}
                    </div>
                    <SeverityBar
                      critical={item.criticalCount ?? 0}
                      high={item.highCount ?? 0}
                      medium={item.mediumCount ?? 0}
                      low={item.lowCount ?? 0}
                    />
                  </div>
                  <div className="text-right">
                    <div className="text-2xl font-semibold tracking-[-0.05em]">
                      {item.currentRiskScore
                        ? Math.round(item.currentRiskScore)
                        : 0}
                    </div>
                  </div>
                </div>
                <div className="mt-3">
                  <Button
                    size="sm"
                    variant="outline"
                    render={
                      <Link to="/devices/$id" params={{ id: item.assetId }} />
                    }
                  >
                    Review device
                  </Button>
                </div>
              </div>
            ))}

            {visibleDevices.length === 0 ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                No owned devices with active pressure were found.
              </div>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </section>
  );
}
