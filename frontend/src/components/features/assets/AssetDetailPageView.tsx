import { useMemo, useState, type ReactNode } from 'react'
import { Link } from "@tanstack/react-router";
import { CircleQuestionMark, Loader2, RotateCcw } from 'lucide-react'
import type { AssetDetail } from '@/api/assets.schemas'
import { Button } from '@/components/ui/button'
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { formatDateTime, formatUnknownValue, looksLikeOpaqueId, startCase } from '@/lib/formatting'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import { toneDot, toneText, type Tone } from '@/lib/tone-classes'

type AssetDetailPageViewProps = {
  asset: AssetDetail
  securityProfiles: SecurityProfile[]
  isAssigningSecurityProfile: boolean
  isSettingCriticality: boolean
  isResettingCriticality: boolean
  onAssignSecurityProfile: (assetId: string, securityProfileId: string | null) => void
  onSetCriticality: (criticality: string) => void
  onResetCriticality: () => void
}

type DetailTab = 'overview' | 'vulnerabilities' | 'software' | 'timeline'

export function AssetDetailPageView({
  asset,
  securityProfiles,
  isAssigningSecurityProfile,
  isSettingCriticality,
  isResettingCriticality,
  onAssignSecurityProfile,
  onSetCriticality,
  onResetCriticality,
}: AssetDetailPageViewProps) {
  const [activeTab, setActiveTab] = useState<DetailTab>("overview");
  const [securityProfileSheetOpen, setSecurityProfileSheetOpen] = useState(false)
  const [criticalitySheetOpen, setCriticalitySheetOpen] = useState(false)
  const metadata = useMemo(
    () => parseMetadata(asset.metadata),
    [asset.metadata],
  );
  const timelineItems = useMemo(() => buildTimelineItems(asset), [asset]);
  const displayTitle =
    asset.assetType === "Device"
      ? asset.deviceComputerDnsName ?? asset.name
      : asset.name;
  const deviceLastSeen = asset.deviceLastSeenAt
    ? formatDateTime(asset.deviceLastSeenAt)
    : "Unknown";

  return (
    <>
      <section className="space-y-5">
        <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
          <div className="grid gap-5 xl:grid-cols-[minmax(0,1.25fr)_minmax(22rem,0.75fr)]">
            <div className="space-y-4">
              <div className="flex flex-wrap gap-2">
                <Pill>{asset.assetType}</Pill>
                <Pill>{asset.criticality} criticality</Pill>
                {asset.assetType === "Device" && asset.deviceHealthStatus ? (
                  <Pill>{asset.deviceHealthStatus}</Pill>
                ) : null}
                {asset.assetType === "Device" && asset.deviceGroupName ? (
                  <Pill>{asset.deviceGroupName}</Pill>
                ) : null}
                {asset.securityProfile ? (
                  <Pill>{asset.securityProfile.name}</Pill>
                ) : null}
              </div>
              <div className="space-y-2">
                <h1 className="text-3xl font-semibold tracking-[-0.04em]">
                  {displayTitle}
                </h1>
                <p className="max-w-3xl text-sm text-muted-foreground">
                  {asset.assetType === "Device"
                    ? [
                        asset.deviceOsPlatform,
                        asset.deviceOsVersion,
                        `Last seen ${deviceLastSeen}`,
                      ]
                        .filter(Boolean)
                        .join(" · ")
                    : (asset.description ??
                      getDefaultDescription(asset.assetType))}
                </p>
              </div>
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {asset.ownerType === "Team" ? (
                  <MetricCard
                    label="Assignment Group"
                    value={asset.ownerTeamName ?? "Unassigned"}
                  />
                ) : null}
                <MetricCard
                  label="Fallback Assignment Group"
                  value={asset.fallbackTeamName ?? "None"}
                />
                {asset.assetType !== "Device" ? (
                  <MetricCard label="Asset Type" value={asset.assetType} />
                ) : null}
              </div>
              <div className="flex flex-wrap gap-2">
                {asset.assetType === "Software" && asset.tenantSoftwareId ? (
                  <Link
                    to="/software/$id"
                    params={{ id: asset.tenantSoftwareId }}
                    search={{
                      page: 1,
                      pageSize: 25,
                      version: "",
                      tab: "overview",
                    }}
                    className="inline-flex rounded-full border border-primary/30 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/15"
                  >
                    Open software workspace
                  </Link>
                ) : null}
                {asset.assetType === "Software" ? (
                  <Link
                    to="/software/$id/remediation"
                    params={{ id: asset.tenantSoftwareId ?? asset.id }}
                    className="inline-flex rounded-full border border-primary/30 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/15"
                  >
                    Open remediation view
                  </Link>
                ) : null}
                {asset.assetType === "Device" && asset.remediation ? (
                  <Link
                    to="/remediation"
                    search={{
                      page: 1,
                      pageSize: 25,
                      search: "",
                      criticality: "",
                      outcome: "",
                      approvalStatus: "",
                      decisionState: "",
                    }}
                    className="inline-flex rounded-full border border-primary/30 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/15"
                  >
                    Open remediation workbench
                  </Link>
                ) : null}
              </div>
            </div>

            <div className="rounded-2xl border border-border/70 bg-card px-4 py-3">
              <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                Reference
              </p>
              <div className="mt-2 grid gap-3 md:grid-cols-2 xl:grid-cols-1">
                <div>
                  <p className="text-xs text-muted-foreground">External ID</p>
                  <code className="mt-1 block text-xs break-all">
                    {asset.externalId}
                  </code>
                </div>
                {asset.assetType === "Device" ? (
                  <div>
                    <p className="text-xs text-muted-foreground">
                      Entra device ID
                    </p>
                    <code className="mt-1 block text-xs break-all">
                      {asset.deviceAadDeviceId ?? "Unknown"}
                    </code>
                  </div>
                ) : null}
              </div>
            </div>
          </div>
        </header>

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)]">
          <section className="rounded-3xl border border-border/70 bg-card p-4">
            <div className="mb-4 flex flex-wrap gap-2">
              <TabButton
                label="Overview"
                active={activeTab === "overview"}
                onClick={() => setActiveTab("overview")}
              />
              <TabButton
                label="Vulnerabilities"
                active={activeTab === "vulnerabilities"}
                onClick={() => setActiveTab("vulnerabilities")}
              />
              <TabButton
                label="Software Inventory"
                active={activeTab === "software"}
                onClick={() => setActiveTab("software")}
              />
              <TabButton
                label="Activity Timeline"
                active={activeTab === "timeline"}
                onClick={() => setActiveTab("timeline")}
              />
            </div>

            {activeTab === "overview" ? (
              <div className="space-y-5">
                {asset.assetType === "Device" && asset.remediation ? (
                  <section className="rounded-2xl border border-border/70 bg-background p-4">
                    <SectionHeader
                      title="Action summary"
                      description="Open remediation work linked to this device and where ownership currently sits."
                    />
                    <div className="mt-4 grid gap-3 md:grid-cols-3">
                      <MetricCard
                        label="Open remediation tasks"
                        value={String(asset.remediation.openTaskCount)}
                      />
                      <MetricCard
                        label="Overdue"
                        value={String(asset.remediation.overdueTaskCount)}
                      />
                      <MetricCard
                        label="Linked vulnerabilities"
                        value={String(asset.vulnerabilities.length)}
                      />
                    </div>
                  </section>
                ) : null}
                {asset.assetType === "Device" ? (
                  <section className="rounded-2xl border border-border/70 bg-background p-4">
                    <SectionHeader
                      title="Device context"
                      description="Operational identity and health data used to understand this endpoint quickly."
                    />
                    <div className="mt-4 grid gap-x-6 gap-y-4 md:grid-cols-2">
                      <DefinitionItem
                        label="Machine name"
                        value={
                          asset.deviceComputerDnsName ?? asset.name ?? "Unknown"
                        }
                      />
                      <DefinitionItem
                        label="Health status"
                        value={asset.deviceHealthStatus ?? "Unknown"}
                      />
                      <DefinitionItem
                        label="OS platform"
                        value={asset.deviceOsPlatform ?? "Unknown"}
                      />
                      <DefinitionItem
                        label="OS version"
                        value={asset.deviceOsVersion ?? "Unknown"}
                      />
                      <DefinitionItem
                        label="Risk score"
                        value={asset.deviceRiskScore ?? "Unknown"}
                      />
                      <DefinitionItem
                        label="Last seen"
                        value={
                          asset.deviceLastSeenAt
                            ? new Date(asset.deviceLastSeenAt).toLocaleString()
                            : "Unknown"
                        }
                      />
                      <DefinitionItem
                        label="Last IP address"
                        value={asset.deviceLastIpAddress ?? "Unknown"}
                      />
                      <DefinitionItem
                        label="Device group"
                        value={asset.deviceGroupName ?? "Unknown"}
                      />
                      <DefinitionItem
                        label="Device value"
                        value={asset.deviceValue ?? "Unknown"}
                      />
                      <DefinitionItem
                        label="Entra device ID"
                        value={asset.deviceAadDeviceId ?? "Unknown"}
                        mono
                      />
                    </div>
                  </section>
                ) : null}
                <section className="grid gap-4 lg:grid-cols-2">
                  <button
                    type="button"
                    className="w-full rounded-2xl border border-border/70 bg-background p-4 text-left transition hover:border-foreground/20 hover:bg-muted/20"
                    onClick={() => setSecurityProfileSheetOpen(true)}
                  >
                    <div className="flex items-start justify-between gap-4">
                      <div className="space-y-2">
                        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                          Environmental severity profile
                        </p>
                        <div className="flex flex-wrap items-center gap-2">
                          <Pill>
                            {asset.securityProfile?.name ??
                              "Vendor severity only"}
                          </Pill>
                          {asset.securityProfile ? (
                            <span className="text-sm text-muted-foreground">
                              {asset.securityProfile.internetReachability}
                            </span>
                          ) : null}
                        </div>
                        <p className="text-sm text-muted-foreground">
                          {asset.securityProfile
                            ? `${asset.securityProfile.environmentClass} profile applied for adjusted vulnerability severity.`
                            : "No profile is applied. Vendor severity is used directly."}
                        </p>
                      </div>
                      <span className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
                        Manage
                      </span>
                    </div>
                  </button>
                  <button
                    type="button"
                    className="w-full rounded-2xl border border-border/70 bg-background p-4 text-left transition hover:border-foreground/20 hover:bg-muted/20"
                    onClick={() => setCriticalitySheetOpen(true)}
                  >
                    <div className="flex items-start justify-between gap-4">
                      <div className="space-y-2">
                        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                          Criticality
                        </p>
                        <div className="flex flex-wrap items-center gap-2">
                          <Pill>{asset.criticality}</Pill>
                          <span className="text-sm text-muted-foreground">
                            {asset.criticalityDetail?.source ?? "Unknown"}
                          </span>
                        </div>
                        <p className="text-sm text-muted-foreground">
                          {asset.criticalityDetail?.reason ??
                            "Used directly by PatchHound risk scoring and prioritization."}
                        </p>
                      </div>
                      <span className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
                        Manage
                      </span>
                    </div>
                  </button>
                </section>
                {asset.assetType === "Software" ? (
                  <section className="rounded-2xl border border-border/70 bg-background p-4">
                    <SectionHeader
                      title="CPE binding"
                      description="Reusable normalized software identity used for NVD-based software correlation."
                    />
                    <div className="mt-4">
                      <SoftwareCpeBindingPanel
                        binding={asset.softwareCpeBinding}
                      />
                    </div>
                    <div className="mt-4">
                      <SectionHeader
                        title="Known vulnerabilities"
                        description="Derived software-level matches for this software asset."
                      />
                      {asset.knownSoftwareVulnerabilities.length === 0 ? (
                        <p className="mt-4 text-sm text-muted-foreground">
                          No known vulnerabilities are currently linked to this
                          software asset.
                        </p>
                      ) : (
                        <div className="mt-4 space-y-3">
                          {asset.knownSoftwareVulnerabilities.map((item) => (
                            <Link
                              key={item.vulnerabilityId}
                              to="/vulnerabilities/$id"
                              params={{ id: item.vulnerabilityId }}
                              className="block rounded-xl border border-border/70 bg-card px-3 py-3 transition hover:border-foreground/20 hover:bg-muted/20"
                            >
                              <div className="flex flex-wrap items-start justify-between gap-3">
                                <div>
                                  <p className="font-medium">{item.title}</p>
                                  <p className="mt-1 text-xs text-muted-foreground">
                                    {item.externalId} • {item.vendorSeverity}
                                    {item.cvssScore !== null
                                      ? ` • CVSS ${item.cvssScore.toFixed(1)}`
                                      : ""}
                                  </p>
                                  <p className="mt-2 text-xs text-muted-foreground">
                                    {item.evidence}
                                  </p>
                                </div>
                                <div className="flex flex-wrap gap-2">
                                  <Pill>{item.matchMethod}</Pill>
                                  <Pill>{item.confidence}</Pill>
                                </div>
                              </div>
                            </Link>
                          ))}
                        </div>
                      )}
                    </div>
                  </section>
                ) : null}
                <section className="rounded-2xl border border-border/70 bg-background p-4">
                  <SectionHeader
                    title="Stored metadata"
                    description="Residual source-specific metadata that has not been normalized into first-class fields."
                  />
                  <div className="mt-4 grid gap-3 md:grid-cols-2">
                    {Object.keys(metadata).length === 0 ? (
                      <p className="text-sm text-muted-foreground">
                        No extra metadata is stored for this asset.
                      </p>
                    ) : (
                      Object.entries(metadata).map(([key, value]) => (
                        <MetricCard
                          key={key}
                          label={startCase(key)}
                          value={formatUnknownValue(value)}
                          mono={
                            typeof value === "string" &&
                            looksLikeOpaqueId(value)
                          }
                        />
                      ))
                    )}
                  </div>
                </section>
              </div>
            ) : null}

            {activeTab === "vulnerabilities" ? (
              <section className="space-y-3">
                {asset.vulnerabilities.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    No linked vulnerabilities recorded for this asset.
                  </p>
                ) : (
                  [...asset.vulnerabilities]
                    .sort(
                      (a, b) =>
                        (b.effectiveScore ?? b.vendorScore ?? 0) -
                        (a.effectiveScore ?? a.vendorScore ?? 0),
                    )
                    .map((vulnerability) => (
                      <Link
                        key={vulnerability.vulnerabilityId}
                        to="/vulnerabilities/$id"
                        params={{ id: vulnerability.vulnerabilityId }}
                        className="block rounded-2xl border border-border/70 bg-background p-4 hover:border-foreground/20 hover:bg-muted/20"
                      >
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div className="space-y-2">
                            <div className="flex flex-wrap items-center gap-2">
                              <p className="font-medium">
                                {vulnerability.title}
                              </p>
                              {vulnerability.episodeCount > 1 ? (
                                <Pill>
                                  Recurred {vulnerability.episodeCount - 1}x
                                </Pill>
                              ) : null}
                            </div>
                            <p className="text-xs text-muted-foreground">
                              {vulnerability.externalId} •{" "}
                              {vulnerability.status}
                            </p>
                            <div className="grid gap-2 sm:grid-cols-2">
                              <MetricCard
                                label="Vendor Severity"
                                value={
                                  vulnerability.vendorScore
                                    ? `${vulnerability.vendorSeverity} (${vulnerability.vendorScore.toFixed(1)})`
                                    : vulnerability.vendorSeverity
                                }
                              />
                              {asset.securityProfile &&
                              vulnerability.effectiveSeverity !==
                                vulnerability.vendorSeverity ? (
                                <MetricCard
                                  label={`Adjusted Severity (${asset.securityProfile.name})`}
                                  value={
                                    vulnerability.effectiveScore
                                      ? `${vulnerability.effectiveSeverity} (${vulnerability.effectiveScore.toFixed(1)})`
                                      : vulnerability.effectiveSeverity
                                  }
                                />
                              ) : null}
                              <MetricCard
                                label="CVSS Vector"
                                value={
                                  vulnerability.cvssVector ?? "Not available"
                                }
                                mono
                              />
                              <MetricCard
                                label="Published"
                                value={
                                  vulnerability.publishedDate
                                    ? new Date(
                                        vulnerability.publishedDate,
                                      ).toLocaleDateString()
                                    : "Unknown"
                                }
                              />
                            </div>
                            <p className="text-sm text-muted-foreground">
                              {vulnerability.description}
                            </p>
                            {vulnerability.assessmentReasonSummary ? (
                              <p className={`text-xs ${toneText("info")}`}>
                                {vulnerability.assessmentReasonSummary}
                              </p>
                            ) : null}
                            <div className="flex flex-wrap gap-1">
                              {vulnerability.episodes.map((episode) => (
                                <Pill key={episode.episodeNumber}>
                                  #{episode.episodeNumber}{" "}
                                  {episode.status === "Open"
                                    ? "open"
                                    : "resolved"}
                                </Pill>
                              ))}
                            </div>
                          </div>
                          <p className="text-xs text-muted-foreground">
                            {new Date(
                              vulnerability.detectedDate,
                            ).toLocaleDateString()}
                          </p>
                        </div>
                      </Link>
                    ))
                )}
              </section>
            ) : null}

            {activeTab === "software" ? (
              <section className="space-y-3">
                {asset.softwareInventory.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    No software inventory is currently linked to this asset.
                  </p>
                ) : (
                  asset.softwareInventory.map((software) => (
                    <div
                      key={software.softwareAssetId}
                      className="rounded-2xl border border-border/70 bg-background p-4"
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          {software.tenantSoftwareId ? (
                            <Link
                              to="/software/$id"
                              params={{ id: software.tenantSoftwareId }}
                              search={{
                                page: 1,
                                pageSize: 25,
                                version: "",
                                tab: "overview",
                              }}
                              className="font-medium hover:text-primary"
                            >
                              {software.name}
                            </Link>
                          ) : (
                            <p className="font-medium">{software.name}</p>
                          )}
                          <p className="mt-1 text-xs text-muted-foreground">
                            {software.externalId}
                          </p>
                        </div>
                        <p className="text-xs text-muted-foreground">
                          Last seen{" "}
                          {new Date(software.lastSeenAt).toLocaleDateString()}
                        </p>
                      </div>
                      <div className="mt-3 flex flex-wrap gap-1">
                        {software.episodes.map((episode) => (
                          <Pill key={episode.episodeNumber}>
                            #{episode.episodeNumber}{" "}
                            {episode.removedAt ? "removed" : "installed"}
                          </Pill>
                        ))}
                      </div>
                      <div className="mt-3">
                        <SoftwareCpeBindingPanel
                          binding={software.cpeBinding}
                          compact
                        />
                      </div>
                    </div>
                  ))
                )}
              </section>
            ) : null}

            {activeTab === "timeline" ? (
              <section className="space-y-3">
                {timelineItems.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    No activity history is available yet.
                  </p>
                ) : (
                  timelineItems.map((item, index) => (
                    <div key={item.id} className="flex gap-3">
                      <div className="flex w-5 flex-col items-center">
                        <span
                          className={`mt-1 h-2.5 w-2.5 rounded-full ${toneDot(item.tone)}`}
                        />
                        {index < timelineItems.length - 1 ? (
                          <span className="mt-1 h-full w-px bg-border/80" />
                        ) : null}
                      </div>
                      <div className="flex-1 rounded-xl border border-border/70 bg-background px-3 py-3">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className="text-sm font-medium">{item.title}</p>
                          <span className="text-xs text-muted-foreground">
                            {new Date(item.at).toLocaleString()}
                          </span>
                        </div>
                        <p className="mt-1 text-sm text-muted-foreground">
                          {item.detail}
                        </p>
                      </div>
                    </div>
                  ))
                )}
              </section>
            ) : null}
          </section>

          <aside className="space-y-4">
            {asset.risk ? (
              <section className="rounded-3xl border border-border/70 bg-card p-4">
                <SectionHeader
                  title="Current risk"
                  description="Current device risk and the open episodes driving it most."
                />
                <div className="mt-4 space-y-3">
                  <div className="flex items-start justify-between gap-3 rounded-2xl border border-border/70 bg-background/40 px-4 py-4">
                    <div>
                      <div className="flex items-center gap-1.5">
                        <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                          Asset risk score
                        </p>
                        {asset.risk.explanation ? (
                          <AssetRiskExplanationPopover asset={asset} />
                        ) : null}
                      </div>
                      <p className="mt-2 text-3xl font-semibold tracking-[-0.04em]">
                        {asset.risk.overallScore.toFixed(0)}
                      </p>
                    </div>
                    <Pill>{asset.risk.riskBand}</Pill>
                  </div>
                  <div className="grid gap-3 sm:grid-cols-3">
                    <MetricCard
                      label="Open episodes"
                      value={String(asset.risk.openEpisodeCount)}
                    />
                    <MetricCard
                      label="Max episode risk"
                      value={asset.risk.maxEpisodeRiskScore.toFixed(0)}
                    />
                    <MetricCard
                      label="High / critical"
                      value={`${asset.risk.highCount + asset.risk.criticalCount}`}
                    />
                  </div>
                  {asset.risk.topDrivers.length > 0 ? (
                    <div className="space-y-2">
                      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                        Highest-risk episodes
                      </p>
                      {asset.risk.topDrivers.slice(0, 3).map((driver) => (
                        <Link
                          key={driver.tenantVulnerabilityId}
                          to="/vulnerabilities/$id"
                          params={{ id: driver.tenantVulnerabilityId }}
                          className="block rounded-xl border border-border/70 bg-background px-3 py-3 transition hover:border-foreground/20 hover:bg-muted/20"
                        >
                          <div className="flex items-start justify-between gap-3">
                            <div className="min-w-0">
                              <div className="flex flex-wrap items-center gap-2">
                                <p className="truncate text-sm font-medium">
                                  {driver.externalId}
                                </p>
                                <Pill>{driver.riskBand}</Pill>
                              </div>
                              <p className="mt-1 line-clamp-2 text-xs text-muted-foreground">
                                {driver.title}
                              </p>
                            </div>
                            <div className="text-right">
                              <p className="text-lg font-semibold tabular-nums">
                                {driver.episodeRiskScore.toFixed(0)}
                              </p>
                              <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                                risk
                              </p>
                            </div>
                          </div>
                        </Link>
                      ))}
                    </div>
                  ) : null}
                </div>
              </section>
            ) : null}
          </aside>
        </div>
      </section>
      <Sheet
        open={securityProfileSheetOpen}
        onOpenChange={setSecurityProfileSheetOpen}
      >
        <SheetContent
          side="right"
          className="w-full border-l border-border/80 bg-card p-0 sm:max-w-md"
        >
          <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
            <SheetTitle>Environmental severity profile</SheetTitle>
            <SheetDescription>
              Reusable environment settings used to recalculate effective
              vulnerability severity for this asset.
            </SheetDescription>
          </SheetHeader>
          <div className="space-y-4 p-4">
            <section className="rounded-2xl border border-border/70 bg-background p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Assigned profile
              </p>
              <div className="mt-3 grid gap-3">
                <select
                  className="rounded-xl border border-input bg-card px-3 py-2.5 text-sm"
                  value={asset.securityProfile?.id ?? ""}
                  onChange={(event) =>
                    onAssignSecurityProfile(
                      asset.id,
                      event.target.value || null,
                    )
                  }
                  disabled={isAssigningSecurityProfile}
                >
                  <option value="">No security profile</option>
                  {securityProfiles.map((profile) => (
                    <option key={profile.id} value={profile.id}>
                      {profile.name} • {profile.internetReachability}
                    </option>
                  ))}
                </select>
                <div className="rounded-xl border border-border/70 bg-card px-3 py-3 text-sm text-muted-foreground">
                  {isAssigningSecurityProfile
                    ? "Applying profile..."
                    : (asset.securityProfile?.name ??
                      "Using vendor severity only")}
                </div>
              </div>
            </section>
            {asset.securityProfile ? (
              <section className="grid gap-3">
                <MetricCard
                  label="Environment Class"
                  value={asset.securityProfile.environmentClass}
                />
                <MetricCard
                  label="Reachability"
                  value={asset.securityProfile.internetReachability}
                />
                <MetricCard
                  label="Confidentiality Requirement"
                  value={asset.securityProfile.confidentialityRequirement}
                />
                <MetricCard
                  label="Integrity Requirement"
                  value={asset.securityProfile.integrityRequirement}
                />
                <MetricCard
                  label="Availability Requirement"
                  value={asset.securityProfile.availabilityRequirement}
                />
              </section>
            ) : (
              <section className="rounded-2xl border border-dashed border-border/70 bg-background px-4 py-5 text-sm text-muted-foreground">
                This asset currently uses vendor severity directly because no
                environmental profile is assigned.
              </section>
            )}
          </div>
        </SheetContent>
      </Sheet>
      <Sheet open={criticalitySheetOpen} onOpenChange={setCriticalitySheetOpen}>
        <SheetContent
          side="right"
          className="w-full border-l border-border/80 bg-card p-0 sm:max-w-md"
        >
          <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
            <SheetTitle>Criticality</SheetTitle>
            <SheetDescription>
              This classification feeds asset risk scoring, prioritization, and
              executive reporting.
            </SheetDescription>
          </SheetHeader>
          <div className="space-y-4 p-4">
            <section className="rounded-2xl border border-border/70 bg-background p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Effective criticality
                  </p>
                  <div className="mt-2 flex items-center gap-2">
                    <Pill>{asset.criticality}</Pill>
                    <span className="text-sm text-muted-foreground">
                      {asset.criticalityDetail?.source ?? "Unknown"}
                    </span>
                  </div>
                </div>
                {asset.criticalityDetail?.source === "ManualOverride" ? (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="size-8 rounded-full border border-border/70 text-muted-foreground hover:text-foreground"
                          disabled={
                            isResettingCriticality || isSettingCriticality
                          }
                          onClick={onResetCriticality}
                          aria-label="Remove manual criticality override"
                        />
                      }
                    >
                      {isResettingCriticality ? (
                        <Loader2 className="size-4 animate-spin" />
                      ) : (
                        <RotateCcw className="size-4" />
                      )}
                    </TooltipTrigger>
                    <TooltipContent>
                      Remove manual override and let rules or baseline
                      criticality take over
                    </TooltipContent>
                  </Tooltip>
                ) : null}
              </div>
              <div className="mt-4">
                <Select
                  value={asset.criticality}
                  onValueChange={(value) => {
                    if (value && value !== asset.criticality) {
                      onSetCriticality(value);
                    }
                  }}
                >
                  <SelectTrigger
                    className="h-9 w-full rounded-xl border-border/70 bg-background/80 px-3"
                    disabled={isSettingCriticality || isResettingCriticality}
                  >
                    {isSettingCriticality ? (
                      <div className="flex items-center gap-2 text-sm text-muted-foreground">
                        <Loader2 className="size-4 animate-spin" />
                        Saving...
                      </div>
                    ) : (
                      <SelectValue />
                    )}
                  </SelectTrigger>
                  <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                    {criticalityOptions.map((value) => (
                      <SelectItem key={value} value={value}>
                        {value}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </section>

            <section className="grid gap-3">
              <MetricCard
                label="Reason"
                value={
                  asset.criticalityDetail?.reason ??
                  "No rule or manual rationale has been recorded."
                }
              />
              <MetricCard
                label="Updated"
                value={
                  asset.criticalityDetail?.updatedAt
                    ? formatUtcTimestamp(asset.criticalityDetail.updatedAt)
                    : "Unknown"
                }
              />
            </section>
          </div>
        </SheetContent>
      </Sheet>
    </>
  );
}

function AssetRiskExplanationPopover({ asset }: { asset: AssetDetail }) {
  const explanation = asset.risk?.explanation
  if (!explanation) {
    return null
  }
  const calculatedAt = asset.risk!.calculatedAt

  return (
    <Popover>
      <PopoverTrigger className="inline-flex items-center rounded-full text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
        <CircleQuestionMark className="size-4" />
      </PopoverTrigger>
      <PopoverContent side="left" align="end" sideOffset={10} className="w-[30rem] gap-3 rounded-2xl p-4">
        <PopoverHeader>
          <PopoverTitle>Asset risk breakdown</PopoverTitle>
          <PopoverDescription>
            This device score is calculated from its top unresolved episode risks and the current severity mix.
          </PopoverDescription>
        </PopoverHeader>

        <div className="grid gap-3 sm:grid-cols-2">
          <MetricCard label="Score" value={explanation.score.toFixed(0)} />
          <MetricCard label="Formula version" value={explanation.calculationVersion} />
          <MetricCard label="Open episodes" value={String(explanation.openEpisodeCount)} />
          <MetricCard label="Max episode risk" value={explanation.maxEpisodeRiskScore.toFixed(0)} />
          <MetricCard label="Top 3 average" value={explanation.topThreeAverage.toFixed(2)} />
          <MetricCard label="Calculated at" value={formatDateTime(calculatedAt)} />
        </div>

        <div className="rounded-2xl border border-border/70 bg-background/60 p-3">
          <p className="text-xs font-medium text-muted-foreground">
            Formula
          </p>
          <p className="mt-2 text-sm leading-relaxed text-foreground/90">
            <span className="font-medium">0.70 × max episode risk</span> + <span className="font-medium">0.20 × top 3 average</span> + severity bonuses
          </p>
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            <MetricCard label="Max episode contribution" value={explanation.maxEpisodeContribution.toFixed(2)} />
            <MetricCard label="Top 3 contribution" value={explanation.topThreeContribution.toFixed(2)} />
            <MetricCard label={`Critical (${explanation.criticalCount})`} value={explanation.criticalContribution.toFixed(2)} />
            <MetricCard label={`High (${explanation.highCount})`} value={explanation.highContribution.toFixed(2)} />
            <MetricCard label={`Medium (${explanation.mediumCount})`} value={explanation.mediumContribution.toFixed(2)} />
            <MetricCard label={`Low (${explanation.lowCount})`} value={explanation.lowContribution.toFixed(2)} />
          </div>
        </div>

        <div className="space-y-2">
          <p className="text-xs font-medium text-muted-foreground">
            Persisted factors
          </p>
          {explanation.factors.map((factor) => (
            <div key={factor.name} className="rounded-xl border border-border/70 bg-background/60 px-3 py-2.5">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm font-medium text-foreground">
                    {factor.name}
                  </p>
                  <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
                    {factor.description}
                  </p>
                </div>
                <span className="text-sm font-semibold tabular-nums text-foreground">
                  {factor.impact.toFixed(2)}
                </span>
              </div>
            </div>
          ))}
        </div>
      </PopoverContent>
    </Popover>
  )
}

function TabButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={active
        ? 'rounded-full border border-primary/30 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary'
        : 'rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm text-muted-foreground hover:bg-muted/20'}
    >
      {label}
    </button>
  )
}

function SectionHeader({ title, description }: { title: string; description: string }) {
  return (
    <div>
      <h2 className="text-lg font-semibold">{title}</h2>
      <p className="mt-1 text-sm text-muted-foreground">{description}</p>
    </div>
  )
}

function MetricCard({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-xl border border-border/70 bg-card px-3 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className={mono ? 'mt-1 break-all font-mono text-sm' : 'mt-1 text-sm font-medium'}>{value}</p>
    </div>
  )
}

function DefinitionItem({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="border-b border-border/50 pb-3 last:border-b-0">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
        {label}
      </p>
      <p className={mono ? 'mt-1 break-all font-mono text-sm' : 'mt-1 text-sm font-medium'}>
        {value}
      </p>
    </div>
  )
}

function Pill({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-full border border-border/70 bg-background px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em]">
      {children}
    </span>
  )
}

function SoftwareCpeBindingPanel({
  binding,
  compact = false,
}: {
  binding: AssetDetail['softwareCpeBinding'] | AssetDetail['softwareInventory'][number]['cpeBinding']
  compact?: boolean
}) {
  if (!binding) {
    return <p className="text-sm text-muted-foreground">No CPE binding has been recorded for this software asset yet.</p>
  }

  return (
    <div className={compact ? 'grid gap-3 md:grid-cols-2' : 'grid gap-3 md:grid-cols-2'}>
      <MetricCard label="CPE 2.3 URI" value={binding.cpe23Uri} mono />
      <MetricCard label="Confidence" value={binding.confidence} />
      <MetricCard label="Binding Method" value={binding.bindingMethod} />
      <MetricCard label="Matched Vendor" value={binding.matchedVendor ?? 'Unknown'} />
      <MetricCard label="Matched Product" value={binding.matchedProduct ?? 'Unknown'} />
      <MetricCard label="Matched Version" value={binding.matchedVersion ?? 'Unknown'} />
      <MetricCard label="Last Validated" value={new Date(binding.lastValidatedAt).toLocaleString()} />
    </div>
  )
}

type TimelineItem = { id: string; at: string; title: string; detail: string; tone: Tone }

function buildTimelineItems(asset: AssetDetail): TimelineItem[] {
  const vulnerabilityEvents = asset.vulnerabilities.flatMap((vulnerability) =>
    vulnerability.episodes.flatMap((episode) => {
      const events: TimelineItem[] = [{
        id: `vuln:${vulnerability.vulnerabilityId}:start:${episode.episodeNumber}`,
        at: episode.firstSeenAt,
        title: `${vulnerability.externalId} detected`,
        detail: `${vulnerability.title} appeared on this asset as episode #${episode.episodeNumber}.`,
        tone: episode.episodeNumber > 1 ? 'warning' : 'info',
      }]
      if (episode.resolvedAt) {
        events.push({
          id: `vuln:${vulnerability.vulnerabilityId}:end:${episode.episodeNumber}`,
          at: episode.resolvedAt,
          title: `${vulnerability.externalId} resolved`,
          detail: `${vulnerability.title} was no longer detected on this asset.`,
          tone: 'neutral',
        })
      }
      return events
    }),
  )

  const softwareEvents = asset.softwareInventory.flatMap((software) =>
    software.episodes.flatMap((episode) => {
      const events: TimelineItem[] = [{
        id: `software:${software.softwareAssetId}:start:${episode.episodeNumber}`,
        at: episode.firstSeenAt,
        title: `${software.name} installed`,
        detail: `${software.externalId} was present in episode #${episode.episodeNumber}.`,
        tone: episode.episodeNumber > 1 ? 'warning' : 'info',
      }]
      if (episode.removedAt) {
        events.push({
          id: `software:${software.softwareAssetId}:end:${episode.episodeNumber}`,
          at: episode.removedAt,
          title: `${software.name} removed`,
          detail: `${software.externalId} was no longer present on the asset.`,
          tone: 'neutral',
        })
      }
      return events
    }),
  )

  return [...vulnerabilityEvents, ...softwareEvents].sort((left, right) => new Date(right.at).getTime() - new Date(left.at).getTime())
}

function parseMetadata(metadata: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(metadata) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed as Record<string, unknown> : {}
  } catch {
    return {}
  }
}

function getDefaultDescription(assetType: string): string {
  switch (assetType) {
    case 'Software':
      return 'Software package tracked through inventory sync and vulnerability correlation.'
    case 'Device':
      return 'Endpoint or host asset tracked through vulnerability and ownership workflows.'
    default:
      return 'Managed asset record tracked inside PatchHound.'
  }
}

function formatUtcTimestamp(value: string): string {
  return new Date(value).toUTCString()
}

const criticalityOptions = ['Critical', 'High', 'Medium', 'Low'] as const
