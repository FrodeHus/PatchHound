import { useMemo, useState, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import { Loader2, RotateCcw } from 'lucide-react'
import type { DeviceDetail } from '@/api/devices.schemas'
import type { DeviceExposure } from '@/api/devices.schemas'
import type { BusinessLabel } from '@/api/business-labels.schemas'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import type { DeviceSoftwareItem } from '@/api/software.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { WorkNotesSheet } from '@/components/features/work-notes/WorkNotesSheet'
import { DeviceAdvancedToolsPanel } from '@/components/features/devices/DeviceAdvancedToolsPanel'
import { formatDateTime, formatUnknownValue, looksLikeOpaqueId, startCase } from '@/lib/formatting'

// Phase 1 canonical cleanup (Task 15): device-native detail page view.
// Software, vulnerabilities, activity timeline, and the rich risk
// explanation popover were removed alongside the legacy AssetDetail
// contract. Phase 5 will rewire those sections onto the canonical
// Device identity and restore the corresponding tabs.

const criticalityOptions = ['Low', 'Medium', 'High', 'Critical']

type DeviceDetailPageViewProps = {
  device: DeviceDetail
  exposures: DeviceExposure[]
  software?: DeviceSoftwareItem[]
  isSoftwareLoading?: boolean
  canUseAdvancedTools: boolean
  securityProfiles: SecurityProfile[]
  availableBusinessLabels: BusinessLabel[]
  isAssigningSecurityProfile: boolean
  isSettingCriticality: boolean
  isResettingCriticality: boolean
  isAssigningBusinessLabels: boolean
  onAssignSecurityProfile: (deviceId: string, securityProfileId: string | null) => void
  onSetCriticality: (criticality: string) => void
  onResetCriticality: () => void
  onAssignBusinessLabels: (businessLabelIds: string[]) => void
}

type DetailTab = 'overview' | 'exposures' | 'software' | 'advanced'

export function DeviceDetailPageView({
  device,
  exposures,
  software = [],
  isSoftwareLoading = false,
  canUseAdvancedTools,
  securityProfiles,
  availableBusinessLabels,
  isAssigningSecurityProfile,
  isSettingCriticality,
  isResettingCriticality,
  isAssigningBusinessLabels,
  onAssignSecurityProfile,
  onSetCriticality,
  onResetCriticality,
  onAssignBusinessLabels,
}: DeviceDetailPageViewProps) {
  const [activeTab, setActiveTab] = useState<DetailTab>('overview')
  const [securityProfileSheetOpen, setSecurityProfileSheetOpen] = useState(false)
  const [criticalitySheetOpen, setCriticalitySheetOpen] = useState(false)
  const [businessLabelsSheetOpen, setBusinessLabelsSheetOpen] = useState(false)
  const metadata = useMemo(() => parseMetadata(device.metadata), [device.metadata])
  const selectedBusinessLabelIds = useMemo(
    () => new Set(device.businessLabels.map((label) => label.id)),
    [device.businessLabels],
  )
  const displayTitle = device.computerDnsName ?? device.name
  const deviceLastSeen = device.lastSeenAt ? formatDateTime(device.lastSeenAt) : 'Unknown'

  return (
    <>
      <section className="space-y-5">
        <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
          <div className="grid gap-5 xl:grid-cols-[minmax(0,1.25fr)_minmax(22rem,0.75fr)]">
            <div className="space-y-4">
              <div className="flex flex-wrap gap-2">
                <Pill>Device</Pill>
                <Pill>{device.criticality} criticality</Pill>
                {device.healthStatus ? <Pill>{device.healthStatus}</Pill> : null}
                {device.groupName ? <Pill>{device.groupName}</Pill> : null}
                {device.securityProfile ? <Pill>{device.securityProfile.name}</Pill> : null}
                {device.businessLabels.slice(0, 3).map((label) => (
                  <BusinessLabelBadge key={label.id} name={label.name} color={label.color} />
                ))}
                {device.businessLabels.length > 3 ? (
                  <Pill>+{device.businessLabels.length - 3} labels</Pill>
                ) : null}
              </div>
              <div className="space-y-2">
                <h1 className="text-3xl font-semibold tracking-[-0.04em]">{displayTitle}</h1>
                <p className="max-w-3xl text-sm text-muted-foreground">
                  {[
                    device.osPlatform,
                    device.osVersion,
                    `Last seen ${deviceLastSeen}`,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </p>
              </div>
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {device.ownerType === 'Team' ? (
                  <MetricCard
                    label="Assignment Group"
                    value={device.ownerTeamName ?? 'Unassigned'}
                  />
                ) : null}
                <MetricCard
                  label="Fallback Assignment Group"
                  value={device.fallbackTeamName ?? 'None'}
                />
              </div>
              <div className="flex flex-wrap gap-2">
                <WorkNotesSheet
                  entityType="assets"
                  entityId={device.id}
                  title="Device work notes"
                  description="Capture tenant-local notes for this device."
                />
                {device.remediation ? (
                  <Link
                    to="/remediation"
                    search={{
                      page: 1,
                      pageSize: 25,
                      search: '',
                      criticality: '',
                      outcome: '',
                      approvalStatus: '',
                      decisionState: '',
                      missedMaintenanceWindow: false,
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
                  <code className="mt-1 block text-xs break-all">{device.externalId}</code>
                </div>
                <div>
                  <p className="text-xs text-muted-foreground">Entra device ID</p>
                  <code className="mt-1 block text-xs break-all">
                    {device.aadDeviceId ?? 'Unknown'}
                  </code>
                </div>
              </div>
            </div>
          </div>
        </header>

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)]">
          <section className="rounded-3xl border border-border/70 bg-card p-4">
            <div className="mb-4 flex flex-wrap gap-2">
              <TabButton
                label="Overview"
                active={activeTab === 'overview'}
                onClick={() => setActiveTab('overview')}
              />
              <TabButton
                label="Exposures"
                active={activeTab === 'exposures'}
                onClick={() => setActiveTab('exposures')}
              />
              <TabButton
                label="Software"
                active={activeTab === 'software'}
                onClick={() => setActiveTab('software')}
              />
              {canUseAdvancedTools ? (
                <TabButton
                  label="Advanced"
                  active={activeTab === 'advanced'}
                  onClick={() => setActiveTab('advanced')}
                />
              ) : null}
            </div>

            {activeTab === 'overview' ? (
              <div className="space-y-5">
                {device.remediation ? (
                  <section className="rounded-2xl border border-border/70 bg-background p-4">
                    <SectionHeader
                      title="Action summary"
                      description="Open remediation work linked to this device and where ownership currently sits."
                    />
                    <div className="mt-4 grid gap-3 md:grid-cols-2">
                      <MetricCard
                        label="Open remediation tasks"
                        value={String(device.remediation.openTaskCount)}
                      />
                      <MetricCard
                        label="Overdue"
                        value={String(device.remediation.overdueTaskCount)}
                      />
                    </div>
                  </section>
                ) : null}
                <section className="rounded-2xl border border-border/70 bg-background p-4">
                  <SectionHeader
                    title="Device context"
                    description="Operational identity and health data used to understand this endpoint quickly."
                  />
                  <div className="mt-4 grid gap-x-6 gap-y-4 md:grid-cols-2">
                    <DefinitionItem
                      label="Machine name"
                      value={device.computerDnsName ?? device.name ?? 'Unknown'}
                    />
                    <DefinitionItem label="Health status" value={device.healthStatus ?? 'Unknown'} />
                    <DefinitionItem label="OS platform" value={device.osPlatform ?? 'Unknown'} />
                    <DefinitionItem label="OS version" value={device.osVersion ?? 'Unknown'} />
                    <DefinitionItem label="Risk score" value={device.riskScore ?? 'Unknown'} />
                    <DefinitionItem
                      label="Last seen"
                      value={
                        device.lastSeenAt
                          ? new Date(device.lastSeenAt).toLocaleString()
                          : 'Unknown'
                      }
                    />
                    <DefinitionItem
                      label="Last IP address"
                      value={device.lastIpAddress ?? 'Unknown'}
                    />
                    <DefinitionItem label="Device group" value={device.groupName ?? 'Unknown'} />
                    <DefinitionItem label="Device value" value={device.deviceValue ?? 'Unknown'} />
                    <DefinitionItem
                      label="Entra device ID"
                      value={device.aadDeviceId ?? 'Unknown'}
                      mono
                    />
                  </div>
                </section>
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
                            {device.securityProfile?.name ?? 'Vendor severity only'}
                          </Pill>
                          {device.securityProfile ? (
                            <span className="text-sm text-muted-foreground">
                              {device.securityProfile.internetReachability}
                            </span>
                          ) : null}
                        </div>
                        <p className="text-sm text-muted-foreground">
                          {device.securityProfile
                            ? `${device.securityProfile.environmentClass} profile applied for adjusted vulnerability severity.`
                            : 'No profile is applied. Vendor severity is used directly.'}
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
                          <Pill>{device.criticality}</Pill>
                          <span className="text-sm text-muted-foreground">
                            {device.criticalityDetail?.source ?? 'Unknown'}
                          </span>
                        </div>
                        <p className="text-sm text-muted-foreground">
                          {device.criticalityDetail?.reason
                            ?? 'Used directly by PatchHound risk scoring and prioritization.'}
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
                    onClick={() => setBusinessLabelsSheetOpen(true)}
                  >
                    <div className="flex items-start justify-between gap-4">
                      <div className="space-y-2">
                        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                          Business labels
                        </p>
                        <div className="flex flex-wrap items-center gap-2">
                          {device.businessLabels.length > 0 ? (
                            device.businessLabels.map((label) => (
                              <BusinessLabelBadge
                                key={label.id}
                                name={label.name}
                                color={label.color}
                              />
                            ))
                          ) : (
                            <Pill>No labels</Pill>
                          )}
                        </div>
                        <p className="text-sm text-muted-foreground">
                          Add recognizable business context such as Production, Finance, Executive,
                          or Customer-facing.
                        </p>
                      </div>
                      <span className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
                        Manage
                      </span>
                    </div>
                  </button>
                </section>
                <section className="rounded-2xl border border-border/70 bg-background p-4">
                  <SectionHeader
                    title="Stored metadata"
                    description="Residual source-specific metadata that has not been normalized into first-class fields."
                  />
                  <div className="mt-4 grid gap-3 md:grid-cols-2">
                    {Object.keys(metadata).length === 0 ? (
                      <p className="text-sm text-muted-foreground">
                        No extra metadata is stored for this device.
                      </p>
                    ) : (
                      Object.entries(metadata).map(([key, value]) => (
                        <MetricCard
                          key={key}
                          label={startCase(key)}
                          value={formatUnknownValue(value)}
                          mono={typeof value === 'string' && looksLikeOpaqueId(value)}
                        />
                      ))
                    )}
                  </div>
                </section>
              </div>
            ) : null}

            {activeTab === 'exposures' ? (              <section className="rounded-2xl border border-border/70 bg-background p-4">
                <SectionHeader
                  title="Exposures"
                  description="Observed vulnerabilities linked to this device from the canonical exposure pipeline."
                />
                {exposures.length === 0 ? (
                  <p className="mt-4 text-sm text-muted-foreground">
                    No exposures observed for this device.
                  </p>
                ) : (
                  <div className="mt-4 overflow-x-auto">
                    <table className="min-w-full border-separate border-spacing-y-2 text-sm">
                      <thead>
                        <tr className="text-left text-xs uppercase tracking-[0.16em] text-muted-foreground">
                          <th className="px-3 py-2">CVE</th>
                          <th className="px-3 py-2">Severity</th>
                          <th className="px-3 py-2">Matched version</th>
                          <th className="px-3 py-2">Status</th>
                          <th className="px-3 py-2">Environmental CVSS</th>
                          <th className="px-3 py-2">First observed</th>
                          <th className="px-3 py-2">Last observed</th>
                        </tr>
                      </thead>
                      <tbody>
                        {exposures.map((exposure) => (
                          <tr key={exposure.exposureId} className="rounded-xl border border-border/70 bg-card">
                            <td className="px-3 py-3 font-medium">{exposure.externalId}</td>
                            <td className="px-3 py-3">{exposure.severity}</td>
                            <td className="px-3 py-3">{exposure.matchedVersion}</td>
                            <td className="px-3 py-3">{exposure.status}</td>
                            <td className="px-3 py-3">{exposure.environmentalCvss?.toFixed(1) ?? 'Unknown'}</td>
                            <td className="px-3 py-3">{formatDateTime(exposure.firstObservedAt)}</td>
                            <td className="px-3 py-3">{formatDateTime(exposure.lastObservedAt)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            ) : null}

            {activeTab === 'software' ? (
              <section className="rounded-2xl border border-border/70 bg-background p-4">
                <SectionHeader
                  title="Installed software"
                  description="Software products observed on this device from the canonical ingestion pipeline."
                />
                {isSoftwareLoading ? (
                  <p className="mt-4 text-sm text-muted-foreground">Loading...</p>
                ) : software.length === 0 ? (
                  <p className="mt-4 text-sm text-muted-foreground">
                    No software installations recorded for this device.
                  </p>
                ) : (
                  <div className="mt-4 overflow-x-auto">
                    <table className="min-w-full border-separate border-spacing-y-2 text-sm">
                      <thead>
                        <tr className="text-left text-xs uppercase tracking-[0.16em] text-muted-foreground">
                          <th className="px-3 py-2">Software</th>
                          <th className="px-3 py-2">Open vulns</th>
                          <th className="px-3 py-2">Last seen</th>
                        </tr>
                      </thead>
                      <tbody>
                        {software.map((install) => (
                          <tr
                            key={install.softwareProductId}
                            className="rounded-xl border border-border/70 bg-card"
                          >
                            <td className="px-3 py-3 font-medium">
                              <Link
                                to="/software/$id"
                                params={{ id: install.softwareProductId }}
                                search={{ page: 1, pageSize: 25, version: '', tab: 'overview' }}
                                className="text-primary hover:underline"
                              >
                                {install.softwareName}
                              </Link>
                            </td>
                            <td className="px-3 py-3">{install.openVulnerabilityCount}</td>
                            <td className="px-3 py-3">{formatDateTime(install.lastSeenAt)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            ) : null}

            {activeTab === 'advanced' && canUseAdvancedTools ? (
              <div className="space-y-5">
                <DeviceAdvancedToolsPanel device={device} />
              </div>
            ) : null}
          </section>

          <aside className="space-y-4">
            {device.risk ? (
              <section className="rounded-3xl border border-border/70 bg-card p-4">
                <SectionHeader
                  title="Current risk"
                  description="Current device risk and the open episodes driving it most."
                />
                <div className="mt-4 space-y-3">
                  <div className="flex items-start justify-between gap-3 rounded-2xl border border-border/70 bg-background/40 px-4 py-4">
                    <div>
                      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                        Device risk score
                      </p>
                      <p className="mt-2 text-3xl font-semibold tracking-[-0.04em]">
                        {device.risk.overallScore.toFixed(0)}
                      </p>
                    </div>
                    <Pill>{device.risk.riskBand}</Pill>
                  </div>
                  <div className="grid gap-3 sm:grid-cols-3">
                    <MetricCard
                      label="Open episodes"
                      value={String(device.risk.openEpisodeCount)}
                    />
                    <MetricCard
                      label="Max episode risk"
                      value={device.risk.maxEpisodeRiskScore.toFixed(0)}
                    />
                    <MetricCard
                      label="High / critical"
                      value={`${device.risk.highCount + device.risk.criticalCount}`}
                    />
                  </div>
                </div>
              </section>
            ) : (
              <section className="rounded-3xl border border-tone-success-border bg-tone-success p-6">
                <div className="flex flex-col items-center gap-3 text-center">
                  <span className="flex size-14 items-center justify-center rounded-full border border-tone-success-border bg-background/60 text-3xl">
                    👍
                  </span>
                  <div className="space-y-1">
                    <p className="text-lg font-semibold tracking-[-0.02em] text-tone-success-foreground">
                      No known vulnerabilities
                    </p>
                    <p className="text-sm text-tone-success-foreground/80">
                      This device has no linked vulnerabilities and no open risk episodes. Keep it
                      up!
                    </p>
                  </div>
                </div>
              </section>
            )}
          </aside>
        </div>
      </section>
      <Sheet open={securityProfileSheetOpen} onOpenChange={setSecurityProfileSheetOpen}>
        <SheetContent
          side="right"
          className="w-full border-l border-border/80 bg-card p-0 sm:max-w-md"
        >
          <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
            <SheetTitle>Environmental severity profile</SheetTitle>
            <SheetDescription>
              Reusable environment settings used to recalculate effective vulnerability severity
              for this device.
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
                  value={device.securityProfile?.id ?? ''}
                  onChange={(event) =>
                    onAssignSecurityProfile(device.id, event.target.value || null)
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
                    ? 'Applying profile...'
                    : device.securityProfile?.name ?? 'Using vendor severity only'}
                </div>
              </div>
            </section>
            {device.securityProfile ? (
              <section className="grid gap-3">
                <MetricCard
                  label="Environment Class"
                  value={device.securityProfile.environmentClass}
                />
                <MetricCard
                  label="Reachability"
                  value={device.securityProfile.internetReachability}
                />
                <MetricCard
                  label="Confidentiality Requirement"
                  value={device.securityProfile.confidentialityRequirement}
                />
                <MetricCard
                  label="Integrity Requirement"
                  value={device.securityProfile.integrityRequirement}
                />
                <MetricCard
                  label="Availability Requirement"
                  value={device.securityProfile.availabilityRequirement}
                />
              </section>
            ) : (
              <section className="rounded-2xl border border-dashed border-border/70 bg-background px-4 py-5 text-sm text-muted-foreground">
                This device currently uses vendor severity directly because no environmental
                profile is assigned.
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
              This classification feeds device risk scoring, prioritization, and executive
              reporting.
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
                    <Pill>{device.criticality}</Pill>
                    <span className="text-sm text-muted-foreground">
                      {device.criticalityDetail?.source ?? 'Unknown'}
                    </span>
                  </div>
                </div>
                {device.criticalityDetail?.source === 'ManualOverride' ? (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="size-8 rounded-full border border-border/70 text-muted-foreground hover:text-foreground"
                          disabled={isResettingCriticality || isSettingCriticality}
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
                      Remove manual override and let rules or baseline criticality take over
                    </TooltipContent>
                  </Tooltip>
                ) : null}
              </div>
              <div className="mt-4">
                <Select
                  value={device.criticality}
                  onValueChange={(value) => {
                    if (value && value !== device.criticality) {
                      onSetCriticality(value)
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
                  device.criticalityDetail?.reason
                  ?? 'No rule or manual rationale has been recorded.'
                }
              />
              <MetricCard
                label="Updated"
                value={
                  device.criticalityDetail?.updatedAt
                    ? formatUtcTimestamp(device.criticalityDetail.updatedAt)
                    : 'Unknown'
                }
              />
            </section>
          </div>
        </SheetContent>
      </Sheet>
      <Sheet open={businessLabelsSheetOpen} onOpenChange={setBusinessLabelsSheetOpen}>
        <SheetContent
          side="right"
          className="w-full border-l border-border/80 bg-card p-0 sm:max-w-md"
        >
          <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
            <SheetTitle>Business labels</SheetTitle>
            <SheetDescription>
              Add tenant-defined labels that make devices more recognizable in dashboards,
              remediation summaries, and ownership reviews.
            </SheetDescription>
          </SheetHeader>
          <div className="space-y-4 p-4">
            <section className="rounded-2xl border border-border/70 bg-background p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Assigned labels
              </p>
              {device.businessLabels.length > 0 ? (
                <div className="mt-3 flex flex-wrap gap-2">
                  {device.businessLabels.map((label) => (
                    <BusinessLabelBadge key={label.id} name={label.name} color={label.color} />
                  ))}
                </div>
              ) : (
                <p className="mt-3 text-sm text-muted-foreground">
                  No business labels are currently assigned to this device.
                </p>
              )}
            </section>

            <section className="rounded-2xl border border-border/70 bg-background p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Available labels
              </p>
              {availableBusinessLabels.length === 0 ? (
                <p className="mt-3 text-sm text-muted-foreground">
                  No business labels exist for this tenant yet. Create them from the admin console
                  first.
                </p>
              ) : (
                <div className="mt-3 flex flex-wrap gap-2">
                  {availableBusinessLabels.map((label) => {
                    const isSelected = selectedBusinessLabelIds.has(label.id)
                    return (
                      <Button
                        key={label.id}
                        type="button"
                        variant={isSelected ? 'default' : 'outline'}
                        className="h-auto rounded-full px-3 py-1.5"
                        disabled={isAssigningBusinessLabels || !label.isActive}
                        onClick={() => {
                          const nextIds = isSelected
                            ? device.businessLabels
                                .filter((item) => item.id !== label.id)
                                .map((item) => item.id)
                            : [
                                ...device.businessLabels.map((item) => item.id),
                                label.id,
                              ]
                          onAssignBusinessLabels(nextIds)
                        }}
                      >
                        <span
                          className="mr-2 inline-flex size-2.5 rounded-full border border-black/10"
                          style={{ backgroundColor: label.color ?? 'var(--muted-foreground)' }}
                        />
                        {label.name}
                        {!label.isActive ? ' (inactive)' : ''}
                      </Button>
                    )
                  })}
                </div>
              )}
            </section>
          </div>
        </SheetContent>
      </Sheet>
    </>
  )
}

function BusinessLabelBadge({ name, color }: { name: string; color: string | null }) {
  return (
    <Badge
      variant="outline"
      className="rounded-full border-border/70 bg-background/60 px-2.5 py-0.5 text-xs text-foreground"
    >
      <span
        className="mr-1.5 inline-flex size-2 rounded-full border border-black/10"
        style={{ backgroundColor: color ?? 'var(--muted-foreground)' }}
      />
      {name}
    </Badge>
  )
}

function TabButton({
  label,
  active,
  onClick,
}: {
  label: string
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        active
          ? 'rounded-full border border-primary/30 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary'
          : 'rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm text-muted-foreground hover:bg-muted/20'
      }
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

function MetricCard({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-card px-3 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className={mono ? 'mt-1 break-all font-mono text-sm' : 'mt-1 text-sm font-medium'}>
        {value}
      </p>
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
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
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

function parseMetadata(metadata: string): Record<string, unknown> {
  try {
    const parsed = JSON.parse(metadata) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : {}
  } catch {
    return {}
  }
}

function formatUtcTimestamp(value: string): string {
  return new Date(value).toUTCString()
}
