import { useMemo, useState, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type { AssetDetail } from '@/api/assets.schemas'
import { formatUnknownValue, looksLikeOpaqueId, startCase } from '@/lib/formatting'
import type { SecurityProfile } from '@/api/security-profiles.schemas'

type AssetDetailPageViewProps = {
  asset: AssetDetail
  securityProfiles: SecurityProfile[]
  isAssigningSecurityProfile: boolean
  onAssignSecurityProfile: (assetId: string, securityProfileId: string | null) => void
}

type DetailTab = 'overview' | 'vulnerabilities' | 'software' | 'timeline'

export function AssetDetailPageView({
  asset,
  securityProfiles,
  isAssigningSecurityProfile,
  onAssignSecurityProfile,
}: AssetDetailPageViewProps) {
  const [activeTab, setActiveTab] = useState<DetailTab>('overview')
  const metadata = useMemo(() => parseMetadata(asset.metadata), [asset.metadata])
  const timelineItems = useMemo(() => buildTimelineItems(asset), [asset])

  return (
    <section className="space-y-5">
      <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-3">
            <Link
              to="/assets"
              search={{
                page: 1,
                pageSize: 25,
                search: '',
                assetType: '',
                criticality: '',
                ownerType: '',
                unassignedOnly: false,
              }}
              className="text-sm text-muted-foreground hover:text-foreground"
            >
              Back to assets
            </Link>
            <div className="flex flex-wrap gap-2">
              <Pill>{asset.assetType}</Pill>
              <Pill>{asset.criticality} criticality</Pill>
              {asset.securityProfile ? <Pill>{asset.securityProfile.name}</Pill> : null}
            </div>
            <div>
              <h1 className="text-3xl font-semibold tracking-[-0.04em]">{asset.name}</h1>
              <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
                {asset.description ?? getDefaultDescription(asset.assetType)}
              </p>
              {asset.assetType === 'Software' && asset.tenantSoftwareId ? (
                <Link
                  to="/software/$id"
                  params={{ id: asset.tenantSoftwareId }}
                  search={{ page: 1, pageSize: 25, version: '' }}
                  className="mt-3 inline-flex rounded-full border border-primary/30 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/15"
                >
                  Open software workspace
                </Link>
              ) : null}
            </div>
          </div>
          <div className="rounded-2xl border border-border/70 bg-background/45 p-4 text-right">
            <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">External ID</p>
            <code className="mt-1 block text-xs">{asset.externalId}</code>
          </div>
        </div>
      </header>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)]">
        <section className="rounded-[28px] border border-border/70 bg-card p-4">
          <div className="mb-4 flex flex-wrap gap-2">
            <TabButton label="Overview" active={activeTab === 'overview'} onClick={() => setActiveTab('overview')} />
            <TabButton label="Vulnerabilities" active={activeTab === 'vulnerabilities'} onClick={() => setActiveTab('vulnerabilities')} />
            <TabButton label="Software Inventory" active={activeTab === 'software'} onClick={() => setActiveTab('software')} />
            <TabButton label="Activity Timeline" active={activeTab === 'timeline'} onClick={() => setActiveTab('timeline')} />
          </div>

          {activeTab === 'overview' ? (
            <div className="space-y-5">
              <SecurityProfilePanel
                asset={asset}
                securityProfiles={securityProfiles}
                isAssigningSecurityProfile={isAssigningSecurityProfile}
                onAssignSecurityProfile={onAssignSecurityProfile}
              />
              <section className="grid gap-3 md:grid-cols-2">
                {asset.ownerType === 'Team' ? (
                  <MetricCard label="Assignment Group" value={asset.ownerTeamId ?? 'Unassigned'} mono />
                ) : (
                  <MetricCard label="Owner User" value={asset.ownerUserId ?? 'Unassigned'} mono />
                )}
                <MetricCard label="Fallback Assignment Group" value={asset.fallbackTeamId ?? 'None'} mono />
              </section>
              {asset.assetType === 'Device' ? (
                <section className="rounded-2xl border border-border/70 bg-background p-4">
                  <SectionHeader title="Device context" description="Normalized telemetry and identification fields captured for this endpoint." />
                  <div className="mt-4 grid gap-3 md:grid-cols-2">
                    <MetricCard label="Machine Name" value={asset.deviceComputerDnsName ?? asset.name ?? 'Unknown'} />
                    <MetricCard label="Health Status" value={asset.deviceHealthStatus ?? 'Unknown'} />
                    <MetricCard label="OS Platform" value={asset.deviceOsPlatform ?? 'Unknown'} />
                    <MetricCard label="OS Version" value={asset.deviceOsVersion ?? 'Unknown'} />
                    <MetricCard label="Risk Score" value={asset.deviceRiskScore ?? 'Unknown'} />
                    <MetricCard label="Last Seen" value={asset.deviceLastSeenAt ? new Date(asset.deviceLastSeenAt).toLocaleString() : 'Unknown'} />
                    <MetricCard label="Last IP Address" value={asset.deviceLastIpAddress ?? 'Unknown'} />
                    <MetricCard label="Entra Device ID" value={asset.deviceAadDeviceId ?? 'Unknown'} mono />
                  </div>
                </section>
              ) : null}
              {asset.assetType === 'Software' ? (
                <section className="rounded-2xl border border-border/70 bg-background p-4">
                  <SectionHeader title="CPE binding" description="Reusable normalized software identity used for NVD-based software correlation." />
                  <div className="mt-4">
                    <SoftwareCpeBindingPanel binding={asset.softwareCpeBinding} />
                  </div>
                  <div className="mt-4">
                    <SectionHeader title="Known vulnerabilities" description="Derived software-level matches for this software asset." />
                    {asset.knownSoftwareVulnerabilities.length === 0 ? (
                      <p className="mt-4 text-sm text-muted-foreground">No known vulnerabilities are currently linked to this software asset.</p>
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
                                  {item.cvssScore !== null ? ` • CVSS ${item.cvssScore.toFixed(1)}` : ''}
                                </p>
                                <p className="mt-2 text-xs text-muted-foreground">{item.evidence}</p>
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
                <SectionHeader title="Stored metadata" description="Residual source-specific metadata that has not been normalized into first-class fields." />
                <div className="mt-4 grid gap-3 md:grid-cols-2">
                  {Object.keys(metadata).length === 0 ? (
                    <p className="text-sm text-muted-foreground">No extra metadata is stored for this asset.</p>
                  ) : (
                    Object.entries(metadata).map(([key, value]) => (
                      <MetricCard key={key} label={startCase(key)} value={formatUnknownValue(value)} mono={typeof value === 'string' && looksLikeOpaqueId(value)} />
                    ))
                  )}
                </div>
              </section>
            </div>
          ) : null}

          {activeTab === 'vulnerabilities' ? (
            <section className="space-y-3">
              {asset.vulnerabilities.length === 0 ? (
                <p className="text-sm text-muted-foreground">No linked vulnerabilities recorded for this asset.</p>
              ) : asset.vulnerabilities.map((vulnerability) => (
                <Link
                  key={vulnerability.vulnerabilityId}
                  to="/vulnerabilities/$id"
                  params={{ id: vulnerability.vulnerabilityId }}
                  className="block rounded-2xl border border-border/70 bg-background p-4 hover:border-foreground/20 hover:bg-muted/20"
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="font-medium">{vulnerability.title}</p>
                        {vulnerability.episodeCount > 1 ? <Pill>Recurred {vulnerability.episodeCount - 1}x</Pill> : null}
                      </div>
                      <p className="text-xs text-muted-foreground">
                        {vulnerability.externalId} • {vulnerability.status}
                      </p>
                      <div className="grid gap-2 sm:grid-cols-2">
                        <MetricCard label="Vendor Severity" value={vulnerability.vendorScore ? `${vulnerability.vendorSeverity} (${vulnerability.vendorScore.toFixed(1)})` : vulnerability.vendorSeverity} />
                        <MetricCard label="CVSS Vector" value={vulnerability.cvssVector ?? 'Not available'} mono />
                        <MetricCard label="Effective Severity" value={vulnerability.effectiveScore ? `${vulnerability.effectiveSeverity} (${vulnerability.effectiveScore.toFixed(1)})` : vulnerability.effectiveSeverity} />
                        <MetricCard label="Published" value={vulnerability.publishedDate ? new Date(vulnerability.publishedDate).toLocaleDateString() : 'Unknown'} />
                      </div>
                      <p className="text-sm text-muted-foreground">{vulnerability.description}</p>
                      {vulnerability.assessmentReasonSummary ? (
                        <p className="text-xs text-sky-700">{vulnerability.assessmentReasonSummary}</p>
                      ) : null}
                      <div className="flex flex-wrap gap-1">
                        {vulnerability.episodes.map((episode) => (
                          <Pill key={episode.episodeNumber}>
                            #{episode.episodeNumber} {episode.status === 'Open' ? 'open' : 'resolved'}
                          </Pill>
                        ))}
                      </div>
                    </div>
                    <p className="text-xs text-muted-foreground">{new Date(vulnerability.detectedDate).toLocaleDateString()}</p>
                  </div>
                </Link>
              ))}
            </section>
          ) : null}

          {activeTab === 'software' ? (
            <section className="space-y-3">
              {asset.softwareInventory.length === 0 ? (
                <p className="text-sm text-muted-foreground">No software inventory is currently linked to this asset.</p>
              ) : asset.softwareInventory.map((software) => (
                <div key={software.softwareAssetId} className="rounded-2xl border border-border/70 bg-background p-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      {software.tenantSoftwareId ? (
                        <Link
                          to="/software/$id"
                          params={{ id: software.tenantSoftwareId }}
                          search={{ page: 1, pageSize: 25, version: '' }}
                          className="font-medium hover:text-primary"
                        >
                          {software.name}
                        </Link>
                      ) : (
                        <p className="font-medium">{software.name}</p>
                      )}
                      <p className="mt-1 text-xs text-muted-foreground">{software.externalId}</p>
                    </div>
                    <p className="text-xs text-muted-foreground">Last seen {new Date(software.lastSeenAt).toLocaleDateString()}</p>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-1">
                    {software.episodes.map((episode) => (
                      <Pill key={episode.episodeNumber}>
                        #{episode.episodeNumber} {episode.removedAt ? 'removed' : 'installed'}
                      </Pill>
                    ))}
                  </div>
                  <div className="mt-3">
                    <SoftwareCpeBindingPanel binding={software.cpeBinding} compact />
                  </div>
                </div>
              ))}
            </section>
          ) : null}

          {activeTab === 'timeline' ? (
            <section className="space-y-3">
              {timelineItems.length === 0 ? (
                <p className="text-sm text-muted-foreground">No activity history is available yet.</p>
              ) : timelineItems.map((item, index) => (
                <div key={item.id} className="flex gap-3">
                  <div className="flex w-5 flex-col items-center">
                    <span className={`mt-1 h-2.5 w-2.5 rounded-full ${item.tone === 'amber' ? 'bg-amber-500' : item.tone === 'blue' ? 'bg-sky-500' : 'bg-slate-400'}`} />
                    {index < timelineItems.length - 1 ? <span className="mt-1 h-full w-px bg-border/80" /> : null}
                  </div>
                  <div className="flex-1 rounded-xl border border-border/70 bg-background px-3 py-3">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <p className="text-sm font-medium">{item.title}</p>
                      <span className="text-xs text-muted-foreground">{new Date(item.at).toLocaleString()}</span>
                    </div>
                    <p className="mt-1 text-sm text-muted-foreground">{item.detail}</p>
                  </div>
                </div>
              ))}
            </section>
          ) : null}
        </section>

        <aside className="space-y-4">
          <section className="rounded-[28px] border border-border/70 bg-card p-4">
            <SectionHeader title="Asset summary" description="Quick operational summary for this asset." />
            <div className="mt-4 grid gap-3">
              <MetricCard label="Linked Vulnerabilities" value={String(asset.vulnerabilities.length)} />
              <MetricCard label="Installed Software" value={String(asset.softwareInventory.length)} />
              <MetricCard label="Security Profile" value={asset.securityProfile?.name ?? 'Not assigned'} />
              <MetricCard label="Asset Type" value={asset.assetType} />
            </div>
          </section>
        </aside>
      </div>
    </section>
  )
}

function SecurityProfilePanel({
  asset,
  securityProfiles,
  isAssigningSecurityProfile,
  onAssignSecurityProfile,
}: {
  asset: AssetDetail
  securityProfiles: SecurityProfile[]
  isAssigningSecurityProfile: boolean
  onAssignSecurityProfile: (assetId: string, securityProfileId: string | null) => void
}) {
  return (
    <section className="rounded-2xl border border-border/70 bg-background p-4">
      <SectionHeader title="Environmental severity profile" description="Reusable environment settings used to recalculate effective vulnerability severity for this asset." />
      <div className="mt-4 grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]">
        <select
          className="rounded-md border border-input bg-card px-3 py-2 text-sm"
          value={asset.securityProfile?.id ?? ''}
          onChange={(event) => onAssignSecurityProfile(asset.id, event.target.value || null)}
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
          {isAssigningSecurityProfile ? 'Applying profile...' : asset.securityProfile?.name ?? 'Using vendor severity only'}
        </div>
      </div>
      {asset.securityProfile ? (
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          <MetricCard label="Environment Class" value={asset.securityProfile.environmentClass} />
          <MetricCard label="Reachability" value={asset.securityProfile.internetReachability} />
          <MetricCard label="Confidentiality Requirement" value={asset.securityProfile.confidentialityRequirement} />
          <MetricCard label="Integrity Requirement" value={asset.securityProfile.integrityRequirement} />
          <MetricCard label="Availability Requirement" value={asset.securityProfile.availabilityRequirement} />
        </div>
      ) : null}
    </section>
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

type TimelineItem = { id: string; at: string; title: string; detail: string; tone: 'blue' | 'amber' | 'slate' }

function buildTimelineItems(asset: AssetDetail): TimelineItem[] {
  const vulnerabilityEvents = asset.vulnerabilities.flatMap((vulnerability) =>
    vulnerability.episodes.flatMap((episode) => {
      const events: TimelineItem[] = [{
        id: `vuln:${vulnerability.vulnerabilityId}:start:${episode.episodeNumber}`,
        at: episode.firstSeenAt,
        title: `${vulnerability.externalId} detected`,
        detail: `${vulnerability.title} appeared on this asset as episode #${episode.episodeNumber}.`,
        tone: episode.episodeNumber > 1 ? 'amber' : 'blue',
      }]
      if (episode.resolvedAt) {
        events.push({
          id: `vuln:${vulnerability.vulnerabilityId}:end:${episode.episodeNumber}`,
          at: episode.resolvedAt,
          title: `${vulnerability.externalId} resolved`,
          detail: `${vulnerability.title} was no longer detected on this asset.`,
          tone: 'slate',
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
        tone: episode.episodeNumber > 1 ? 'amber' : 'blue',
      }]
      if (episode.removedAt) {
        events.push({
          id: `software:${software.softwareAssetId}:end:${episode.episodeNumber}`,
          at: episode.removedAt,
          title: `${software.name} removed`,
          detail: `${software.externalId} was no longer present on the asset.`,
          tone: 'slate',
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
