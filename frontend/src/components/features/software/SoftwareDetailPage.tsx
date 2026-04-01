import { type ReactNode, useState } from 'react'
import { Link } from "@tanstack/react-router";
import { CircleQuestionMark, ShieldAlert, LayoutList, Sparkles } from 'lucide-react'
import type {
  TenantSoftwareDetail,
  TenantSoftwareVulnerability,
  PagedTenantSoftwareInstallations,
} from '@/api/software.schemas'
import type { DecisionContext } from '@/api/remediation.schemas'
import { SoftwareAiReportTab } from '@/components/features/software/SoftwareAiReportTab'
import { SoftwareDescriptionPanel } from '@/components/features/software/SoftwareDescriptionPanel'
import { VersionCohortChooser } from '@/components/features/software/VersionCohortChooser'
import { SoftwareRemediationView } from '@/components/features/remediation/SoftwareRemediationView'
import { WorkNotesSheet } from '@/components/features/work-notes/WorkNotesSheet'
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from '@/components/ui/popover'
import { formatDate, formatDateTime, startCase } from '@/lib/formatting'
import { toneBadge, toneText } from '@/lib/tone-classes'
import {
  approvalStatusTone,
  outcomeTone,
  outcomeLabel,
} from '@/components/features/remediation/remediation-utils'

type TabId = 'overview' | 'remediation' | 'ai'

type SoftwareDetailPageProps = {
  detail: TenantSoftwareDetail
  selectedVersion: string
  installations: PagedTenantSoftwareInstallations
  vulnerabilities: TenantSoftwareVulnerability[]
  activeTab: TabId
  onTabChange: (tab: TabId) => void
  onSelectVersion: (version: string) => void
  onPageChange: (page: number) => void
  canViewRemediation: boolean
  remediationData: DecisionContext | null
  isRemediationLoading?: boolean
  remediationError?: boolean
  tenantSoftwareId: string
}

export function SoftwareDetailPage({
  detail,
  selectedVersion,
  installations,
  vulnerabilities,
  activeTab,
  onTabChange,
  onSelectVersion,
  onPageChange,
  canViewRemediation,
  remediationData,
  isRemediationLoading = false,
  remediationError = false,
  tenantSoftwareId,
}: SoftwareDetailPageProps) {
  const activeVersion =
    detail.versionCohorts.find(
      (cohort) => normalizeVersion(cohort.version) === selectedVersion,
    ) ??
    detail.versionCohorts[0] ??
    null;

  const pendingApproval = remediationData?.currentDecision?.approvalStatus === 'PendingApproval'
  const [renderedAt] = useState(() => Date.now())
  const maintenanceWindowDate = remediationData?.currentDecision?.maintenanceWindowDate ?? null
  const maintenanceWindowHasPassed = maintenanceWindowDate
    ? new Date(maintenanceWindowDate).getTime() < renderedAt
    : false
  const maintenanceWindowMissed = Boolean(
    maintenanceWindowDate
      && maintenanceWindowHasPassed
      && detail.activeVulnerabilityCount > 0
  )
  const supplyChainInsight = detail.supplyChainInsight
  const isComponent = detail.category === 'Component'

  return (
    <section className="space-y-5">
      <header className="overflow-hidden rounded-[1.75rem] border border-border/70 bg-[linear-gradient(130deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_42%),linear-gradient(180deg,color-mix(in_oklab,var(--foreground)_4%,transparent),transparent_58%),var(--color-card)] p-5">
        <div className="grid gap-5 xl:grid-cols-[minmax(0,1.15fr)_minmax(320px,0.85fr)] xl:items-start">
          <div className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <Pill>{detail.normalizationMethod}</Pill>
              <Pill>{detail.confidence} confidence</Pill>
              {detail.primaryCpe23Uri ? (
                <Pill>CPE bound</Pill>
              ) : (
                <Pill>Heuristic identity</Pill>
              )}
              {canViewRemediation && remediationData?.currentDecision ? (
                <span className={`rounded-full border px-3 py-1 text-xs ${
                  pendingApproval
                    ? toneBadge(approvalStatusTone('PendingApproval'))
                    : toneBadge(outcomeTone(remediationData.currentDecision.outcome))
                }`}>
                  {pendingApproval
                    ? 'Approval pending'
                    : outcomeLabel(remediationData.currentDecision.outcome)}
                </span>
              ) : null}
            </div>

            <div className="space-y-2">
              <div className="flex flex-wrap items-end gap-x-3 gap-y-2">
                <h1 className="text-[2.15rem] font-semibold tracking-[-0.05em] leading-none">
                  {startCase(detail.canonicalName)}
                </h1>
                {detail.canonicalVendor ? (
                  <span className="text-sm font-medium text-muted-foreground">
                    {startCase(detail.canonicalVendor)}
                  </span>
                ) : null}
                {isComponent ? (
                  <span className="rounded-full border border-amber-500/30 bg-amber-500/12 px-2.5 py-0.5 text-xs font-medium text-amber-700 dark:text-amber-300">
                    Component
                  </span>
                ) : null}
              </div>
              <p className="max-w-3xl text-sm leading-relaxed text-muted-foreground">
                {detail.activeVulnerabilityCount > 0
                  ? `${detail.activeVulnerabilityCount} open vulnerabilities across ${detail.versionCount} version cohorts and ${detail.activeInstallCount} active installs.`
                  : `${detail.versionCount} version cohorts and ${detail.activeInstallCount} active installs are currently tracked for this software identity.`}
                {' '}
                {detail.exposureImpactScore != null
                  ? `Exposure impact is ${detail.exposureImpactScore.toFixed(1)}.`
                  : 'Exposure impact is not yet scored.'}
              </p>
            </div>

            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
              <HeaderMetaChip
                label="First seen"
                value={detail.firstSeenAt ? formatDate(detail.firstSeenAt) : 'Unknown'}
              />
              <HeaderMetaChip
                label="Last seen"
                value={detail.lastSeenAt ? formatDate(detail.lastSeenAt) : 'Unknown'}
              />
              <HeaderMetaChip
                label="Primary CPE"
                value={detail.primaryCpe23Uri ? 'Bound' : 'Not bound'}
              />
              <HeaderMetaChip
                label="Source aliases"
                value={String(detail.sourceAliases.length)}
              />
            </div>

            <div className="flex flex-wrap gap-2">
              <WorkNotesSheet
                entityType="software"
                entityId={tenantSoftwareId}
                title="Software work notes"
                description="Capture tenant-local notes for this software record."
              />
            </div>

            {maintenanceWindowDate ? (
              <section
                className={`rounded-[1.15rem] border px-4 py-3 ${
                  maintenanceWindowMissed
                    ? 'border-destructive/35 bg-destructive/10'
                    : 'border-border/70 bg-background/55'
                }`}
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p
                      className={`text-[11px] uppercase tracking-[0.18em] ${
                        maintenanceWindowMissed
                          ? 'text-destructive'
                          : 'text-muted-foreground'
                      }`}
                    >
                      Maintenance window
                    </p>
                    <p
                      className={`mt-2 text-sm ${
                        maintenanceWindowMissed ? 'font-semibold text-destructive' : 'text-foreground'
                      }`}
                    >
                      {formatDateTime(maintenanceWindowDate)}
                    </p>
                  </div>
                  <div className="max-w-xl text-sm">
                    {maintenanceWindowMissed ? (
                      <p className="text-destructive">
                        This date has passed, but {detail.activeVulnerabilityCount} open vulnerabilit{detail.activeVulnerabilityCount === 1 ? 'y remains' : 'ies remain'} in scope. That suggests the planned maintenance did not complete as expected.
                      </p>
                    ) : (
                      <p className="text-muted-foreground">
                        Planned date for patch execution to be in place for this software scope.
                      </p>
                    )}
                  </div>
                </div>
              </section>
            ) : null}

            {isComponent ? (
              <section className="rounded-[1.15rem] border border-amber-500/25 bg-amber-500/10 px-4 py-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="max-w-3xl">
                    <p className="text-[11px] uppercase tracking-[0.18em] text-amber-700 dark:text-amber-300">
                      Component software
                    </p>
                    <p className="mt-2 text-sm font-semibold text-foreground">
                      This software is used inside other software suites and usually cannot be patched individually.
                    </p>
                    <p className="mt-2 text-sm text-muted-foreground">
                      Remediation typically requires the implementing product to ship or receive an updated version rather than patching this component directly on its own.
                    </p>
                  </div>
                </div>
              </section>
            ) : null}

            {supplyChainInsight ? (
              <section className="rounded-[1.15rem] border border-amber-500/25 bg-amber-500/10 px-4 py-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="max-w-3xl">
                    <p className="text-[11px] uppercase tracking-[0.18em] text-amber-700 dark:text-amber-300">
                      Supply-chain guidance
                    </p>
                    <p className="mt-2 text-sm font-semibold text-foreground">
                      {startCase(supplyChainInsight.remediationPath.replace(/([A-Z])/g, ' $1').trim())}
                    </p>
                    <p className="mt-2 text-sm text-muted-foreground">
                      {supplyChainInsight.summary}
                    </p>
                  </div>
                  <div className="grid min-w-[220px] gap-1 text-sm text-muted-foreground">
                    <span>
                      Confidence: <span className="font-medium text-foreground">{startCase(supplyChainInsight.confidence)}</span>
                    </span>
                    {supplyChainInsight.primaryComponentName ? (
                      <span>
                        Component:{' '}
                        <span className="font-medium text-foreground">
                          {supplyChainInsight.primaryComponentVersion
                            ? `${supplyChainInsight.primaryComponentName} ${supplyChainInsight.primaryComponentVersion}`
                            : supplyChainInsight.primaryComponentName}
                        </span>
                      </span>
                    ) : null}
                    {supplyChainInsight.fixedVersion ? (
                      <span>
                        Suggested fixed version:{' '}
                        <span className="font-medium text-foreground">{supplyChainInsight.fixedVersion}</span>
                      </span>
                    ) : null}
                    {supplyChainInsight.affectedVulnerabilityCount != null ? (
                      <span>
                        Vulnerabilities in scope:{' '}
                        <span className="font-medium text-foreground">{supplyChainInsight.affectedVulnerabilityCount}</span>
                      </span>
                    ) : null}
                    {supplyChainInsight.sourceFormat ? (
                      <span>
                        Source: <span className="font-medium text-foreground">{supplyChainInsight.sourceFormat}</span>
                      </span>
                    ) : null}
                  </div>
                </div>
              </section>
            ) : null}

          </div>

          <aside className="rounded-[1.35rem] border border-border/70 bg-background/50 p-4">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                  Scope snapshot
                </p>
                <p className="mt-1 text-sm text-muted-foreground">
                  Current tenant-wide footprint for this normalized software.
                </p>
              </div>
            </div>
            <div className="mt-4 grid grid-cols-2 gap-3">
              <HeaderStat
                label="Active installs"
                value={String(detail.activeInstallCount)}
              />
              <HeaderStat
                label="Devices"
                value={String(detail.uniqueDeviceCount)}
              />
              <HeaderStat
                label="Vulnerable installs"
                value={String(detail.vulnerableInstallCount)}
                tone={detail.vulnerableInstallCount > 0 ? "danger" : "neutral"}
              />
              <HeaderStat
                label="Open vulnerabilities"
                value={String(detail.activeVulnerabilityCount)}
                tone={detail.activeVulnerabilityCount > 0 ? "warning" : "neutral"}
              />
              <HeaderStat
                label="Version cohorts"
                value={String(detail.versionCount)}
              />
              <HeaderStat
                label="Exposure impact"
                value={detail.exposureImpactScore != null ? detail.exposureImpactScore.toFixed(1) : "—"}
                info={detail.exposureImpactExplanation ? (
                  <ImpactScoreExplanationPopover detail={detail} />
                ) : null}
                tone={
                  detail.exposureImpactScore == null
                    ? "neutral"
                    : detail.exposureImpactScore >= 75
                      ? "danger"
                      : detail.exposureImpactScore >= 40
                        ? "warning"
                        : detail.exposureImpactScore >= 10
                          ? "info"
                          : "success"
                }
              />
            </div>
          </aside>
        </div>
      </header>

      {/* Top-level tab bar */}
      <nav className="flex items-center gap-1 rounded-full border border-border/70 bg-card p-1">
        <TopTab
          isActive={activeTab === 'overview'}
          onClick={() => onTabChange('overview')}
          icon={<LayoutList className="size-3.5" />}
          label="Overview"
        />
        {canViewRemediation ? (
          <TopTab
            isActive={activeTab === 'remediation'}
            onClick={() => onTabChange('remediation')}
            icon={<ShieldAlert className="size-3.5" />}
            label="Remediation"
            badge={remediationData?.currentDecision ? (
              <span className={`ml-1.5 inline-flex rounded-full border px-1.5 py-0.5 text-[10px] font-medium leading-none ${
                pendingApproval
                  ? toneBadge(approvalStatusTone('PendingApproval'))
                  : toneBadge(outcomeTone(remediationData.currentDecision.outcome))
              }`}>
                {pendingApproval
                  ? 'Pending'
                  : outcomeLabel(remediationData.currentDecision.outcome)}
              </span>
            ) : remediationData ? (
              <span className="ml-1.5 inline-flex rounded-full border border-border/70 bg-muted px-1.5 py-0.5 text-[10px] font-medium leading-none text-muted-foreground">
                No decision
              </span>
            ) : null}
          />
        ) : null}
        <TopTab
          isActive={activeTab === 'ai'}
          onClick={() => onTabChange('ai')}
          icon={<Sparkles className="size-3.5" />}
          label="AI Insights"
        />
      </nav>

      {/* Tab content */}
      {activeTab === 'overview' ? (
        <OverviewTab
          detail={detail}
          selectedVersion={selectedVersion}
          activeVersion={activeVersion}
          installations={installations}
          vulnerabilities={vulnerabilities}
          onSelectVersion={onSelectVersion}
          onPageChange={onPageChange}
        />
      ) : activeTab === 'remediation' && canViewRemediation ? (
        remediationData ? (
          <SoftwareRemediationView
            data={remediationData}
            tenantSoftwareId={tenantSoftwareId}
            embedded
            initialSoftwareDetail={detail}
            initialInstallations={installations}
            initialDeviceVersion={selectedVersion}
          />
        ) : (
          <section className="rounded-2xl border border-border/70 bg-card p-8">
            <div className="mx-auto max-w-2xl text-center">
              <h2 className="text-lg font-semibold tracking-tight">Remediation context unavailable</h2>
              <p className="mt-2 text-sm text-muted-foreground">
                {isRemediationLoading
                  ? 'Loading remediation context for this software title.'
                  : remediationError
                    ? 'The remediation view could not be loaded for this software title right now.'
                    : 'No remediation context is currently available for this software title.'}
              </p>
            </div>
          </section>
        )
      ) : activeTab === 'ai' ? (
        <AiInsightsTab detail={detail} />
      ) : null}
    </section>
  );
}

/* ── Overview Tab ────────────────────────────────────────────── */

function OverviewTab({
  detail,
  selectedVersion,
  activeVersion,
  installations,
  vulnerabilities,
  onSelectVersion,
  onPageChange,
}: {
  detail: TenantSoftwareDetail
  selectedVersion: string
  activeVersion: TenantSoftwareDetail['versionCohorts'][number] | null
  installations: PagedTenantSoftwareInstallations
  vulnerabilities: TenantSoftwareVulnerability[]
  onSelectVersion: (version: string) => void
  onPageChange: (page: number) => void
}) {
  return (
    <>
      {/* Version Pressure Rail */}
      <section className="rounded-2xl border border-border/70 bg-card p-5">
        <VersionCohortChooser
          title="Version cohorts"
          description="Choose a cohort to inspect installs, owners, and vulnerabilities."
          cohorts={detail.versionCohorts}
          selectedVersion={selectedVersion}
          onSelectVersion={onSelectVersion}
          formatVersion={formatVersion}
          normalizeVersion={normalizeVersion}
        />
      </section>

      {/* Two-column layout */}
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]">
        <div className="space-y-4">
          {/* Cohort Installations */}
          <section className="rounded-2xl border border-border/70 bg-card p-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold">Cohort installations</h2>
                <p className="text-sm text-muted-foreground">
                  Paged installations for the selected version cohort.
                </p>
              </div>
              {activeVersion ? (
                <div className="rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm">
                  {formatVersion(activeVersion.version)}
                </div>
              ) : null}
            </div>

            {activeVersion ? (
              <div className="mt-4 grid gap-3 sm:grid-cols-3">
                <Metric
                  label="Installs"
                  value={String(activeVersion.activeInstallCount)}
                />
                <Metric
                  label="Devices"
                  value={String(activeVersion.deviceCount)}
                />
                <Metric
                  label="Open vulns"
                  value={String(activeVersion.activeVulnerabilityCount)}
                  tone={
                    activeVersion.activeVulnerabilityCount > 0
                      ? "warning"
                      : "neutral"
                  }
                />
              </div>
            ) : null}

            {installations.items.length === 0 ? (
              <p className="mt-6 text-sm text-muted-foreground">
                No installations found for this cohort.
              </p>
            ) : (
              <div className="mt-5 overflow-hidden rounded-xl border border-border/70">
                <table className="min-w-full divide-y divide-border/70 text-sm">
                  <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
                    <tr>
                      <th className="px-4 py-3">Device</th>
                      <th className="px-4 py-3">Criticality</th>
                      <th className="px-4 py-3">Profile</th>
                      <th className="px-4 py-3">Open vulns</th>
                      <th className="px-4 py-3">Last seen</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/60 bg-background">
                    {installations.items.map((item) => (
                      <tr
                        key={`${item.deviceAssetId}-${item.softwareAssetId}`}
                        className="align-top"
                      >
                        <td className="px-4 py-3">
                          <div>
                            <Link
                              to="/assets/$id"
                              params={{ id: item.deviceAssetId }}
                              className="font-medium hover:text-primary"
                            >
                              {item.deviceName}
                            </Link>
                            <p className="mt-1 text-xs text-muted-foreground">
                              {item.softwareAssetName} • episode #
                              {item.currentEpisodeNumber}
                            </p>
                          </div>
                        </td>
                        <td className="px-4 py-3 text-muted-foreground">
                          {item.deviceCriticality}
                        </td>
                        <td className="px-4 py-3 text-muted-foreground">
                          {item.securityProfileName ?? "None"}
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={
                              item.openVulnerabilityCount > 0
                                ? toneText("warning")
                                : toneText("success")
                            }
                          >
                            {item.openVulnerabilityCount}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-muted-foreground">
                          {formatDateTime(item.lastSeenAt)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="mt-4 flex items-center justify-between gap-3">
              <p className="text-sm text-muted-foreground">
                Page {installations.page} of{" "}
                {Math.max(installations.totalPages, 1)}
              </p>
              <div className="flex gap-2">
                <PageButton
                  disabled={installations.page <= 1}
                  onClick={() => onPageChange(installations.page - 1)}
                  label="Previous"
                />
                <PageButton
                  disabled={
                    installations.page >= Math.max(installations.totalPages, 1)
                  }
                  onClick={() => onPageChange(installations.page + 1)}
                  label="Next"
                />
              </div>
            </div>
          </section>

          {/* Known Vulnerabilities */}
          <section className="rounded-2xl border border-border/70 bg-card p-5">
            <h2 className="text-lg font-semibold">Known vulnerabilities</h2>
            <p className="text-sm text-muted-foreground">
              Vulnerability matches projected for this software identity.
            </p>
            {vulnerabilities.length === 0 ? (
              <p className="mt-5 text-sm text-muted-foreground">
                No vulnerability matches are currently projected for this
                software.
              </p>
            ) : (
              <div className="mt-5 space-y-3">
                {vulnerabilities.map((item) => (
                  <Link
                    key={item.tenantVulnerabilityId}
                    to="/vulnerabilities/$id"
                    params={{ id: item.tenantVulnerabilityId }}
                    className="block rounded-xl border border-border/70 bg-background p-4 hover:border-foreground/20 hover:bg-muted/20"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="space-y-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <p className="font-medium">{item.title}</p>
                          <Pill>{item.vendorSeverity}</Pill>
                          {item.bestConfidence ? (
                            <Pill>{item.bestConfidence}</Pill>
                          ) : null}
                        </div>
                        <p className="text-xs text-muted-foreground">
                          {item.externalId} • {item.source}
                          {item.cvssScore !== null
                            ? ` • CVSS ${item.cvssScore.toFixed(1)}`
                            : ""}
                        </p>
                        <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
                          <span>{item.affectedInstallCount} installs</span>
                          <span>{item.affectedDeviceCount} devices</span>
                          <span>{item.affectedVersionCount} versions</span>
                        </div>
                        <div className="flex flex-wrap gap-1">
                          {item.affectedVersions.map((version) => (
                            <span
                              key={version}
                              className={
                                normalizeVersion(version) === selectedVersion
                                  ? "rounded-full border border-primary/30 bg-primary/10 px-2 py-1 text-xs text-primary"
                                  : "rounded-full border border-border/70 bg-card px-2 py-1 text-xs text-muted-foreground"
                              }
                            >
                              {version}
                            </span>
                          ))}
                        </div>
                        {item.evidence[0] ? (
                          <p className="text-xs text-muted-foreground">
                            {item.evidence[0].evidence}
                          </p>
                        ) : null}
                      </div>
                      <div className="text-right text-xs text-muted-foreground">
                        <p>{item.bestMatchMethod}</p>
                        <p className="mt-1">
                          Seen {formatDate(item.lastSeenAt)}
                        </p>
                      </div>
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </section>
        </div>

        {/* Right aside */}
        <aside className="space-y-4">
          <section className="rounded-2xl border border-border/70 bg-card p-5">
            <h2 className="text-lg font-semibold">Identity rail</h2>
            <div className="mt-4 grid gap-3">
              <Metric
                label="Canonical vendor"
                value={
                  detail.canonicalVendor
                    ? startCase(detail.canonicalVendor)
                    : "Unknown"
                }
              />
              <Metric
                label="First seen"
                value={
                  detail.firstSeenAt
                    ? formatDate(detail.firstSeenAt)
                    : "Unknown"
                }
              />
              <Metric
                label="Last seen"
                value={
                  detail.lastSeenAt ? formatDate(detail.lastSeenAt) : "Unknown"
                }
              />
              <Metric
                label="Primary CPE"
                value={detail.primaryCpe23Uri ?? "Not bound"}
                mono
              />
            </div>
          </section>

          {detail.lifecycle ? (
            <SoftwareLifecycleSection lifecycle={detail.lifecycle} />
          ) : null}

          <section className="rounded-2xl border border-border/70 bg-card p-5">
            <h2 className="text-lg font-semibold">Source identities</h2>
            <div className="mt-4 space-y-3">
              {detail.sourceAliases.map((alias) => (
                <div
                  key={`${alias.sourceSystem}-${alias.externalSoftwareId}`}
                  className="rounded-2xl border border-border/70 bg-background p-4"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="font-medium">{alias.rawName}</p>
                    <Pill>{alias.sourceSystem}</Pill>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {alias.externalSoftwareId}
                  </p>
                  <div className="mt-3 grid gap-2">
                    <Metric
                      label="Vendor"
                      value={alias.rawVendor ?? "Unknown"}
                    />
                    <Metric
                      label="Version"
                      value={alias.rawVersion ?? "Unknown"}
                    />
                    <Metric label="Match" value={alias.matchReason} />
                  </div>
                </div>
              ))}
            </div>
          </section>
        </aside>
      </div>
    </>
  )
}

/* ── AI Insights Tab ─────────────────────────────────────────── */

function AiInsightsTab({ detail }: { detail: TenantSoftwareDetail }) {
  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]">
      <SoftwareAiReportTab tenantSoftwareId={detail.id} />
      <SoftwareDescriptionPanel
        tenantSoftwareId={detail.id}
        initialDescription={detail.description}
        initialGeneratedAt={detail.descriptionGeneratedAt}
        initialProviderType={detail.descriptionProviderType}
        initialProfileName={detail.descriptionProfileName}
        initialModel={detail.descriptionModel}
      />
    </div>
  )
}

/* ── Shared sub-components ───────────────────────────────────── */

function TopTab({
  isActive,
  label,
  icon,
  badge,
  onClick,
}: {
  isActive: boolean
  label: string
  icon: ReactNode
  badge?: ReactNode
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        isActive
          ? 'inline-flex items-center gap-1.5 rounded-full bg-primary px-4 py-2 text-sm font-medium text-primary-foreground'
          : 'inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-sm text-muted-foreground hover:bg-muted'
      }
    >
      {icon}
      {label}
      {badge}
    </button>
  )
}

function Metric({
  label,
  value,
  mono = false,
  tone = 'neutral',
}: {
  label: string
  value: string
  mono?: boolean
  tone?: 'neutral' | 'success' | 'info' | 'warning' | 'danger'
}) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className={`mt-2 text-sm ${mono ? 'font-mono text-xs break-all' : 'font-medium'} ${toneText(tone)}`}>
        {value}
      </p>
    </div>
  )
}

function HeaderStat({
  label,
  value,
  tone = 'neutral',
  info,
}: {
  label: string
  value: string
  tone?: 'neutral' | 'success' | 'info' | 'warning' | 'danger'
  info?: ReactNode
}) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background/70 px-4 py-3">
      <div className="flex items-center justify-between gap-2">
        <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
        {info}
      </div>
      <p className={`mt-2 text-2xl font-semibold tracking-[-0.04em] ${toneText(tone)}`}>
        {value}
      </p>
    </div>
  )
}

function HeaderMetaChip({
  label,
  value,
}: {
  label: string
  value: string
}) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background/55 px-3 py-2.5">
      <p className="text-[10px] uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </div>
  )
}

function SoftwareLifecycleSection({ lifecycle }: { lifecycle: NonNullable<TenantSoftwareDetail['lifecycle']> }) {
  const now = new Date()
  const eolDate = lifecycle.eolDate ? new Date(lifecycle.eolDate) : null
  const supportEnd = lifecycle.supportEndDate ? new Date(lifecycle.supportEndDate) : null
  const isEol = eolDate ? eolDate <= now : false
  const isSupportEnded = supportEnd ? supportEnd <= now : false

  return (
    <section className="rounded-2xl border border-border/70 bg-card p-5">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-lg font-semibold">Lifecycle</h2>
        <div className="flex flex-wrap items-center gap-1.5">
          {lifecycle.isLts ? (
            <span className="rounded-full border border-tone-success-border bg-tone-success px-2.5 py-0.5 text-[11px] font-medium text-tone-success-foreground">
              LTS
            </span>
          ) : null}
          {lifecycle.isDiscontinued ? (
            <span className="rounded-full border border-destructive/25 bg-destructive/10 px-2.5 py-0.5 text-[11px] font-medium text-destructive">
              Discontinued
            </span>
          ) : isEol ? (
            <span className="rounded-full border border-destructive/25 bg-destructive/10 px-2.5 py-0.5 text-[11px] font-medium text-destructive">
              End of life
            </span>
          ) : null}
        </div>
      </div>
      <div className="mt-4 grid gap-3">
        <Metric
          label="End of life"
          value={eolDate ? formatDate(lifecycle.eolDate!) : 'Unknown'}
          tone={isEol ? 'danger' : 'neutral'}
        />
        <Metric
          label="Active support ends"
          value={supportEnd ? formatDate(lifecycle.supportEndDate!) : 'Unknown'}
          tone={isSupportEnded ? 'warning' : 'neutral'}
        />
        {lifecycle.latestVersion ? (
          <Metric label="Latest version" value={lifecycle.latestVersion} />
        ) : null}
        {lifecycle.enrichedAt ? (
          <Metric label="Last enriched" value={formatDate(lifecycle.enrichedAt)} />
        ) : null}
      </div>
      {lifecycle.productSlug ? (
        <p className="mt-3 text-[11px] text-muted-foreground">
          Source: endoflife.date/{lifecycle.productSlug}
        </p>
      ) : null}
    </section>
  )
}

function Pill({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-full border border-border/70 bg-background px-3 py-1 text-xs text-muted-foreground">
      {children}
    </span>
  )
}

function PageButton({
  disabled,
  label,
  onClick,
}: {
  disabled: boolean
  label: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm text-muted-foreground transition hover:bg-muted/20 disabled:cursor-not-allowed disabled:opacity-50"
    >
      {label}
    </button>
  )
}

function normalizeVersion(version: string | null) {
  return version?.trim() ?? ''
}

function formatVersion(version: string | null) {
  return version && version.trim().length > 0 ? version : 'Unknown version'
}

function ImpactScoreExplanationPopover({ detail }: { detail: TenantSoftwareDetail }) {
  const explanation = detail.exposureImpactExplanation
  if (!explanation) {
    return null
  }

  return (
    <Popover>
      <PopoverTrigger className="inline-flex items-center rounded-full text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
        <CircleQuestionMark className="size-4" />
      </PopoverTrigger>
      <PopoverContent side="left" align="end" sideOffset={10} className="w-[30rem] gap-3 rounded-2xl p-4">
        <PopoverHeader>
          <PopoverTitle>Exposure impact breakdown</PopoverTitle>
          <PopoverDescription>
            Formula {explanation.calculationVersion}. This score reflects the current software footprint in this tenant.
          </PopoverDescription>
        </PopoverHeader>

        <div className="grid gap-2 sm:grid-cols-2">
          <BreakdownMetric label="Open vulnerabilities" value={String(explanation.vulnerabilityCount)} />
          <BreakdownMetric label="Affected devices" value={String(explanation.deviceCount)} />
          <BreakdownMetric label="High-value devices" value={`${explanation.highValueDeviceCount} (${(explanation.highValueRatio * 100).toFixed(0)}%)`} />
          <BreakdownMetric label="Final score" value={explanation.score.toFixed(1)} tone="info" />
        </div>

        <div className="rounded-xl border border-border/70 bg-background/70 p-3">
          <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">Calculation</p>
          <div className="mt-2 space-y-1.5 text-sm text-muted-foreground">
            <p>Vulnerability sum: <span className="font-medium text-foreground">{explanation.rawVulnerabilitySum.toFixed(2)}</span></p>
            <p>Diminished vulnerability component: <span className="font-medium text-foreground">{explanation.vulnerabilityComponent.toFixed(2)}</span></p>
            <p>Device reach weight: <span className="font-medium text-foreground">{explanation.deviceReachWeight.toFixed(2)}</span></p>
            <p>High-value bonus: <span className="font-medium text-foreground">{explanation.highValueBonus.toFixed(2)}</span></p>
            <p>Raw score before clamp: <span className="font-medium text-foreground">{explanation.rawScore.toFixed(2)}</span></p>
          </div>
        </div>

        <div className="rounded-xl border border-border/70 bg-background/70">
          <div className="border-b border-border/70 px-3 py-2">
            <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">Vulnerability inputs</p>
          </div>
          <div className="max-h-64 overflow-auto">
            <table className="min-w-full text-xs">
              <thead className="sticky top-0 bg-background/95 text-left uppercase tracking-[0.12em] text-muted-foreground backdrop-blur">
                <tr>
                  <th className="px-3 py-2 font-medium">Vulnerability</th>
                  <th className="px-3 py-2 font-medium">Severity</th>
                  <th className="px-3 py-2 font-medium">CVSS</th>
                  <th className="px-3 py-2 font-medium">Weight</th>
                  <th className="px-3 py-2 font-medium">Contribution</th>
                </tr>
              </thead>
              <tbody>
                {explanation.vulnerabilityFactors.map((factor) => (
                  <tr key={factor.externalId} className="border-t border-border/60">
                    <td className="px-3 py-2 font-medium text-foreground">{factor.externalId}</td>
                    <td className="px-3 py-2 text-muted-foreground">{startCase(factor.severity)}</td>
                    <td className="px-3 py-2 text-muted-foreground">
                      {factor.cvssScore != null ? factor.cvssScore.toFixed(1) : factor.normalizedScore.toFixed(2)}
                    </td>
                    <td className="px-3 py-2 text-muted-foreground">{factor.severityWeight.toFixed(1)}</td>
                    <td className="px-3 py-2 font-medium text-foreground">{factor.contribution.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </PopoverContent>
    </Popover>
  )
}

function BreakdownMetric({
  label,
  value,
  tone = 'neutral',
}: {
  label: string
  value: string
  tone?: 'neutral' | 'success' | 'info' | 'warning' | 'danger'
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/70 px-3 py-2.5">
      <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className={`mt-1.5 text-sm font-medium ${toneText(tone)}`}>{value}</p>
    </div>
  )
}
