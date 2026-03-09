import { useMemo, useState, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type { AssetDetail } from '@/api/assets.schemas'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'

type AssetDetailPaneProps = {
  asset: AssetDetail | null
  securityProfiles: SecurityProfile[]
  isLoading: boolean
  isAssigningSecurityProfile: boolean
  isAssigningSoftwareCpeBinding: boolean
  isOpen: boolean
  onAssignSecurityProfile: (assetId: string, securityProfileId: string | null) => void
  onAssignSoftwareCpeBinding: (assetId: string, cpe23Uri: string | null) => void
  onOpenChange: (open: boolean) => void
}

type MetadataRecord = Record<string, unknown>

export function AssetDetailPane({
  asset,
  securityProfiles,
  isLoading,
  isAssigningSecurityProfile,
  isAssigningSoftwareCpeBinding,
  isOpen,
  onAssignSecurityProfile,
  onAssignSoftwareCpeBinding,
  onOpenChange,
}: AssetDetailPaneProps) {
  const metadata = useMemo(() => parseMetadata(asset?.metadata), [asset?.metadata])

  return (
    <Sheet open={isOpen} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-background/98 p-0 sm:max-w-2xl">
        <SheetHeader className="border-b border-border/70 bg-muted/20">
          <SheetTitle>{asset?.name ?? 'Asset detail'}</SheetTitle>
          <SheetDescription>
            Inspect operational context, ownership, and type-specific signals without leaving the asset table.
          </SheetDescription>
        </SheetHeader>

        <div className="space-y-6 p-4">
          {isLoading ? (
            <div className="space-y-3">
              <SkeletonBlock className="h-24" />
              <SkeletonBlock className="h-40" />
              <SkeletonBlock className="h-32" />
            </div>
          ) : null}

          {!isLoading && !asset ? (
            <section className="rounded-2xl border border-dashed border-border/70 bg-muted/20 p-6 text-sm text-muted-foreground">
              Select an asset to inspect.
            </section>
          ) : null}

          {!isLoading && asset ? (
            <>
              <section className="rounded-2xl border border-border/70 bg-card p-4 shadow-sm">
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div className="space-y-2">
                    <div className="flex flex-wrap gap-2">
                      <Badge tone="slate">{asset.assetType}</Badge>
                      <Badge tone={asset.criticality === 'Critical' ? 'amber' : 'blue'}>
                        {asset.criticality} criticality
                      </Badge>
                    </div>
                    <h2 className="text-2xl font-semibold tracking-[-0.03em]">{asset.name}</h2>
                    <p className="max-w-2xl text-sm text-muted-foreground">
                      {asset.description ?? getDefaultDescription(asset.assetType)}
                    </p>
                    <Link
                      to="/assets/$id"
                      params={{ id: asset.id }}
                      className="inline-flex rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm font-medium text-foreground hover:bg-muted/20"
                    >
                      Open detail view
                    </Link>
                  </div>
                  <div className="rounded-xl border border-border/70 bg-background px-3 py-2 text-right">
                    <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                      External ID
                    </p>
                    <code className="text-xs">{asset.externalId}</code>
                  </div>
                </div>
              </section>

              <section className="grid gap-3 md:grid-cols-2">
                {asset.ownerType === 'Team' ? (
                  <DataCard label="Assignment Group" value={asset.ownerTeamId ?? 'Unassigned'} mono />
                ) : (
                  <DataCard label="Owner User" value={asset.ownerUserId ?? 'Unassigned'} mono />
                )}
                <DataCard label="Fallback Assignment Group" value={asset.fallbackTeamId ?? 'None'} mono />
              </section>

              {asset.assetType === 'Device' ? (
                <DeviceSecurityProfileSection
                  asset={asset}
                  securityProfiles={securityProfiles}
                  isAssigningSecurityProfile={isAssigningSecurityProfile}
                  onAssignSecurityProfile={onAssignSecurityProfile}
                />
              ) : null}

              {asset.assetType === 'Software' ? (
                <SoftwareSection asset={asset} metadata={metadata} />
              ) : asset.assetType === 'Device' ? (
                <DeviceSection asset={asset} metadata={metadata} />
              ) : (
                <GenericMetadataSection metadata={metadata} />
              )}

              {asset.assetType === 'Device' ? <DeviceActivityTimeline asset={asset} /> : null}

              <section className="rounded-2xl border border-border/70 bg-card p-4">
                <div className="mb-4 flex items-end justify-between gap-3">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                      Exposure
                    </p>
                    <h3 className="text-lg font-semibold">Linked vulnerabilities</h3>
                  </div>
                  <div className="rounded-xl bg-muted/40 px-3 py-1 text-sm">
                    {asset.vulnerabilities.length} linked
                  </div>
                </div>

                {asset.vulnerabilities.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    No active or historical vulnerability links recorded for this asset.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {asset.vulnerabilities.map((vulnerability) => (
                      <Link
                        key={vulnerability.vulnerabilityId}
                        to="/vulnerabilities/$id"
                        params={{ id: vulnerability.vulnerabilityId }}
                        className="block rounded-xl border border-border/70 bg-background px-3 py-3 transition hover:border-foreground/20 hover:bg-muted/20"
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <div className="flex flex-wrap items-center gap-2">
                              <p className="font-medium">{vulnerability.title}</p>
                              {vulnerability.episodeCount > 1 ? (
                                <span className="rounded-full border border-amber-300/70 bg-amber-50 px-2 py-0.5 text-[11px] font-medium uppercase tracking-[0.14em] text-amber-900">
                                  Recurred {vulnerability.episodeCount - 1}x
                                </span>
                              ) : null}
                            </div>
                            <p className="mt-1 text-xs text-muted-foreground">
                              {vulnerability.externalId} • {vulnerability.vendorSeverity} • {vulnerability.status}
                            </p>
                            <div className="mt-2 grid gap-2 sm:grid-cols-2">
                              <DataCard
                                label="Vendor Severity"
                                value={vulnerability.vendorScore
                                  ? `${vulnerability.vendorSeverity} (${vulnerability.vendorScore.toFixed(1)})`
                                  : vulnerability.vendorSeverity}
                              />
                              <DataCard
                                label="CVSS Vector"
                                value={vulnerability.cvssVector ?? 'Not available'}
                              />
                              <DataCard
                                label="Effective Severity"
                                value={vulnerability.effectiveScore
                                  ? `${vulnerability.effectiveSeverity} (${vulnerability.effectiveScore.toFixed(1)})`
                                  : vulnerability.effectiveSeverity}
                              />
                              <DataCard
                                label="Published"
                                value={vulnerability.publishedDate
                                  ? new Date(vulnerability.publishedDate).toLocaleDateString()
                                  : 'Unknown'}
                              />
                            </div>
                            <p className="mt-2 text-sm text-muted-foreground">
                              {vulnerability.description}
                            </p>
                            {vulnerability.assessmentReasonSummary ? (
                              <p className="mt-2 text-xs text-sky-700">
                                {vulnerability.assessmentReasonSummary}
                              </p>
                            ) : null}
                            {vulnerability.possibleCorrelatedSoftware.length > 0 ? (
                              <p className="mt-2 text-xs text-amber-700">
                                Possible correlation: {vulnerability.possibleCorrelatedSoftware.join(', ')}
                              </p>
                            ) : null}
                            <div className="mt-2 flex flex-wrap gap-1">
                              {vulnerability.episodes.map((episode) => (
                                <span key={episode.episodeNumber} className="rounded-full border border-border/70 bg-muted/20 px-2 py-0.5 text-[11px] text-muted-foreground">
                                  #{episode.episodeNumber} {episode.status === 'Open' ? 'open' : 'resolved'}
                                </span>
                              ))}
                            </div>
                          </div>
                          <p className="text-xs text-muted-foreground">
                            {new Date(vulnerability.detectedDate).toLocaleDateString()}
                          </p>
                        </div>
                      </Link>
                    ))}
                  </div>
                )}
              </section>
            </>
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
  )
}

function DeviceSecurityProfileSection({
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
    <section className="rounded-2xl border border-border/70 bg-card p-4">
      <SectionHeader
        eyebrow="Security profile"
        title="Environmental severity profile"
        description="Apply a reusable device environment profile to recalculate effective vulnerability severity."
      />
      <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto]">
        <select
          className="rounded-md border border-input bg-background px-3 py-2 text-sm"
          value={asset.securityProfile?.id ?? ''}
          onChange={(event) => {
            onAssignSecurityProfile(asset.id, event.target.value || null)
          }}
          disabled={isAssigningSecurityProfile}
        >
          <option value="">No security profile</option>
          {securityProfiles.map((profile) => (
            <option key={profile.id} value={profile.id}>
              {profile.name} • {profile.internetReachability}
            </option>
          ))}
        </select>
        <div className="rounded-xl border border-border/70 bg-background px-3 py-3 text-sm text-muted-foreground">
          {isAssigningSecurityProfile ? 'Applying profile...' : asset.securityProfile?.name ?? 'Using vendor severity only'}
        </div>
      </div>
      {asset.securityProfile ? (
        <div className="mt-3 grid gap-3 md:grid-cols-2">
          <DataCard label="Environment Class" value={asset.securityProfile.environmentClass} />
          <DataCard label="Reachability" value={asset.securityProfile.internetReachability} />
          <DataCard label="Confidentiality Requirement" value={asset.securityProfile.confidentialityRequirement} />
          <DataCard label="Integrity Requirement" value={asset.securityProfile.integrityRequirement} />
          <DataCard label="Availability Requirement" value={asset.securityProfile.availabilityRequirement} />
        </div>
      ) : null}
    </section>
  )
}

function SoftwareSection({ asset, metadata }: { asset: AssetDetail; metadata: MetadataRecord }) {
  return (
    <section className="rounded-2xl border border-border/70 bg-card p-4">
      <SectionHeader
        eyebrow="Software signals"
        title="Inventory intelligence"
        description="Package-specific telemetry from the last Defender software inventory sync."
      />
      <div className="grid gap-3 md:grid-cols-2">
        <DataCard label="Vendor" value={readString(metadata.vendor) ?? 'Unknown'} />
        <DataCard label="Version" value={readString(metadata.version) ?? 'Unknown'} />
        <DataCard label="Exposed Machines" value={readNumber(metadata.exposedMachines) ?? '-'} />
        <DataCard label="Impact Score" value={readNumber(metadata.impactScore) ?? '-'} />
        <DataCard label="Weaknesses" value={readNumber(metadata.weaknesses) ?? '-'} />
        <DataCard
          label="Public Exploit"
          value={readBoolean(metadata.publicExploit) ? 'Observed' : 'Not reported'}
        />
      </div>
      <div className="mt-4">
        <SectionHeader
          eyebrow="CPE binding"
          title="Normalized product identity"
          description="The reusable CPE identity PatchHound will use for NVD-based software matching."
        />
        <SoftwareCpeBindingSummary
          binding={asset.softwareCpeBinding}
          canEdit
          isSaving={isAssigningSoftwareCpeBinding}
          onSave={(cpe23Uri) => onAssignSoftwareCpeBinding(asset.id, cpe23Uri)}
        />
      </div>
      <div className="mt-4">
        <SectionHeader
          eyebrow="Known vulnerabilities"
          title="Matched software vulnerabilities"
          description="Derived software-level matches for this software asset based on direct Defender correlation."
        />
        {asset.knownSoftwareVulnerabilities.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No known vulnerabilities are currently linked to this software asset.
          </p>
        ) : (
          <div className="mt-3 space-y-2">
            {asset.knownSoftwareVulnerabilities.map((item) => (
              <Link
                key={item.vulnerabilityId}
                to="/vulnerabilities/$id"
                params={{ id: item.vulnerabilityId }}
                className="block rounded-xl border border-border/70 bg-background px-3 py-3 transition hover:border-foreground/20 hover:bg-muted/20"
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
                    <span className="rounded-full border border-emerald-300/60 bg-emerald-50 px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em] text-emerald-950">
                      {item.matchMethod}
                    </span>
                    <span className="rounded-full border border-sky-300/60 bg-sky-50 px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em] text-sky-900">
                      {item.confidence}
                    </span>
                  </div>
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>
    </section>
  )
}

function DeviceSection({
  asset,
  metadata,
}: {
  asset: AssetDetail
  metadata: MetadataRecord
}) {
  const normalizedFields = [
    { label: 'Machine Name', value: asset.deviceComputerDnsName ?? asset.name ?? 'Unknown' },
    { label: 'Health Status', value: asset.deviceHealthStatus ?? 'Unknown' },
    { label: 'OS Platform', value: asset.deviceOsPlatform ?? 'Unknown' },
    { label: 'OS Version', value: asset.deviceOsVersion ?? 'Unknown' },
    { label: 'Risk Score', value: asset.deviceRiskScore ?? 'Unknown' },
    { label: 'Last Seen', value: asset.deviceLastSeenAt ? new Date(asset.deviceLastSeenAt).toLocaleString() : 'Unknown' },
    { label: 'Last IP Address', value: asset.deviceLastIpAddress ?? 'Unknown' },
    { label: 'Entra Device ID', value: asset.deviceAadDeviceId ?? 'Unknown', mono: true },
  ]

  return (
    <section className="rounded-2xl border border-border/70 bg-card p-4">
      <SectionHeader
        eyebrow="Device telemetry"
        title="Host context"
        description="Normalized machine fields captured from the Defender device inventory."
      />
      <div className="grid gap-3 md:grid-cols-2">
        {normalizedFields.map((field) => (
          <DataCard key={field.label} label={field.label} value={field.value} mono={field.mono === true} />
        ))}
      </div>
      {Object.keys(metadata).length > 0 ? (
        <div className="mt-4">
          <SectionHeader
            eyebrow="Additional metadata"
            title="Stored context"
            description="Any remaining type-specific data that has not been normalized yet."
          />
          <KeyValueGrid metadata={metadata} />
        </div>
      ) : null}
      <div className="mt-4">
        <SectionHeader
          eyebrow="Software history"
          title="Installed software"
          description="Current software inventory with install/remove episodes for correlation."
        />
        {asset.softwareInventory.length === 0 ? (
          <p className="text-sm text-muted-foreground">No software inventory is currently linked to this device.</p>
        ) : (
          <div className="space-y-2">
            {asset.softwareInventory.map((software) => (
              <div key={software.softwareAssetId} className="rounded-xl border border-border/70 bg-background px-3 py-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="font-medium">{software.name}</p>
                    <p className="mt-1 text-xs text-muted-foreground">{software.externalId}</p>
                  </div>
                  <p className="text-xs text-muted-foreground">Last seen {new Date(software.lastSeenAt).toLocaleDateString()}</p>
                </div>
                <div className="mt-2 flex flex-wrap gap-1">
                  {software.episodes.map((episode) => (
                    <span key={episode.episodeNumber} className="rounded-full border border-border/70 bg-muted/20 px-2 py-0.5 text-[11px] text-muted-foreground">
                      #{episode.episodeNumber} {episode.removedAt ? 'removed' : 'installed'}
                    </span>
                  ))}
                </div>
                <div className="mt-3">
                  <SoftwareCpeBindingSummary binding={software.cpeBinding} compact />
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  )
}

function SoftwareCpeBindingSummary({
  binding,
  compact = false,
  canEdit = false,
  isSaving = false,
  onSave,
}: {
  binding: AssetDetail['softwareCpeBinding'] | AssetDetail['softwareInventory'][number]['cpeBinding']
  compact?: boolean
  canEdit?: boolean
  isSaving?: boolean
  onSave?: (cpe23Uri: string | null) => void
}) {
  const [value, setValue] = useState(binding?.cpe23Uri ?? '')

  if (!binding) {
    return (
      <div className="space-y-3">
        <p className="text-sm text-muted-foreground">
          No CPE binding has been recorded for this software asset yet.
        </p>
        {canEdit && onSave ? (
          <SoftwareCpeBindingEditor
            value={value}
            isSaving={isSaving}
            onValueChange={setValue}
            onSave={() => onSave(value.trim() ? value.trim() : null)}
          />
        ) : null}
      </div>
    )
  }

  return (
    <div className="space-y-3">
      <div className={compact ? 'grid gap-3 md:grid-cols-2' : 'grid gap-3 md:grid-cols-2'}>
        <DataCard label="CPE 2.3 URI" value={binding.cpe23Uri} mono />
        <DataCard label="Confidence" value={binding.confidence} />
        <DataCard label="Binding Method" value={binding.bindingMethod} />
        <DataCard label="Matched Vendor" value={binding.matchedVendor ?? 'Unknown'} />
        <DataCard label="Matched Product" value={binding.matchedProduct ?? 'Unknown'} />
        <DataCard label="Matched Version" value={binding.matchedVersion ?? 'Unknown'} />
        <DataCard
          label="Last Validated"
          value={new Date(binding.lastValidatedAt).toLocaleString()}
        />
      </div>
      {canEdit && onSave ? (
        <SoftwareCpeBindingEditor
          value={value}
          isSaving={isSaving}
          onValueChange={setValue}
          onSave={() => onSave(value.trim() ? value.trim() : null)}
        />
      ) : null}
    </div>
  )
}

function SoftwareCpeBindingEditor({
  value,
  isSaving,
  onValueChange,
  onSave,
}: {
  value: string
  isSaving: boolean
  onValueChange: (value: string) => void
  onSave: () => void
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background p-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Manual binding</p>
      <p className="mt-1 text-sm text-muted-foreground">
        Override or clear the software asset’s normalized CPE identity.
      </p>
      <div className="mt-3 flex flex-col gap-2">
        <input
          className="rounded-md border border-input bg-card px-3 py-2 text-sm"
          placeholder="cpe:2.3:a:vendor:product:version:*:*:*:*:*:*:*"
          value={value}
          onChange={(event) => onValueChange(event.target.value)}
          disabled={isSaving}
        />
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onSave}
            disabled={isSaving}
            className="rounded-full border border-border/70 bg-card px-3 py-1.5 text-sm font-medium hover:bg-muted/20 disabled:opacity-60"
          >
            {isSaving ? 'Saving…' : 'Save binding'}
          </button>
          <button
            type="button"
            onClick={() => onValueChange('')}
            disabled={isSaving}
            className="rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm text-muted-foreground hover:bg-muted/20 disabled:opacity-60"
          >
            Clear value
          </button>
        </div>
      </div>
    </div>
  )
}

type DeviceActivityItem = {
  id: string
  at: string
  title: string
  detail: string
  tone: 'blue' | 'amber' | 'slate'
}

function DeviceActivityTimeline({ asset }: { asset: AssetDetail }) {
  const items = useMemo<DeviceActivityItem[]>(() => {
    const vulnerabilityEvents = asset.vulnerabilities.flatMap((vulnerability) =>
      vulnerability.episodes.flatMap((episode) => {
        const events: DeviceActivityItem[] = [
          {
            id: `vuln:${vulnerability.vulnerabilityId}:start:${episode.episodeNumber}`,
            at: episode.firstSeenAt,
            title: `${vulnerability.externalId} detected`,
            detail: `${vulnerability.title} appeared on this device as episode #${episode.episodeNumber}.`,
            tone: episode.episodeNumber > 1 ? 'amber' : 'blue',
          },
        ]

        if (episode.resolvedAt) {
          events.push({
            id: `vuln:${vulnerability.vulnerabilityId}:end:${episode.episodeNumber}`,
            at: episode.resolvedAt,
            title: `${vulnerability.externalId} resolved`,
            detail: `${vulnerability.title} was no longer detected on this device.`,
            tone: 'slate',
          })
        }

        return events
      }),
    )

    const softwareEvents = asset.softwareInventory.flatMap((software) =>
      software.episodes.flatMap((episode) => {
        const events: DeviceActivityItem[] = [
          {
            id: `software:${software.softwareAssetId}:start:${episode.episodeNumber}`,
            at: episode.firstSeenAt,
            title: `${software.name} installed`,
            detail: `${software.externalId} was present on the device in episode #${episode.episodeNumber}.`,
            tone: episode.episodeNumber > 1 ? 'amber' : 'blue',
          },
        ]

        if (episode.removedAt) {
          events.push({
            id: `software:${software.softwareAssetId}:end:${episode.episodeNumber}`,
            at: episode.removedAt,
            title: `${software.name} removed`,
            detail: `${software.externalId} was no longer present on the device.`,
            tone: 'slate',
          })
        }

        return events
      }),
    )

    return [...vulnerabilityEvents, ...softwareEvents].sort(
      (left, right) => new Date(right.at).getTime() - new Date(left.at).getTime(),
    )
  }, [asset])

  if (items.length === 0) {
    return null
  }

  return (
    <section className="rounded-2xl border border-border/70 bg-card p-4">
      <SectionHeader
        eyebrow="Timeline"
        title="Device activity"
        description="Merged vulnerability and software history to explain what changed on this device over time."
      />
      <div className="space-y-3">
        {items.map((item, index) => (
          <div key={item.id} className="flex gap-3">
            <div className="flex w-5 flex-col items-center">
              <span
                className={`mt-1 h-2.5 w-2.5 rounded-full ${
                  item.tone === 'amber'
                    ? 'bg-amber-500'
                    : item.tone === 'blue'
                      ? 'bg-sky-500'
                      : 'bg-slate-400'
                }`}
              />
              {index < items.length - 1 ? <span className="mt-1 h-full w-px bg-border/80" /> : null}
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
      </div>
    </section>
  )
}

function GenericMetadataSection({ metadata }: { metadata: MetadataRecord }) {
  return (
    <section className="rounded-2xl border border-border/70 bg-card p-4">
      <SectionHeader
        eyebrow="Metadata"
        title="Stored context"
        description="Type-specific data persisted on the asset record."
      />
      {Object.keys(metadata).length === 0 ? (
        <p className="text-sm text-muted-foreground">No additional metadata is stored for this asset.</p>
      ) : (
        <KeyValueGrid metadata={metadata} />
      )}
    </section>
  )
}

function KeyValueGrid({ metadata }: { metadata: MetadataRecord }) {
  return (
    <div className="grid gap-3 md:grid-cols-2">
      {Object.entries(metadata).map(([key, value]) => (
        <DataCard key={key} label={startCase(key)} value={formatValue(value)} mono={typeof value === 'string' && looksLikeId(value)} />
      ))}
    </div>
  )
}

function SectionHeader({
  eyebrow,
  title,
  description,
}: {
  eyebrow: string
  title: string
  description: string
}) {
  return (
    <div className="mb-4 space-y-1">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{eyebrow}</p>
      <h3 className="text-lg font-semibold">{title}</h3>
      <p className="text-sm text-muted-foreground">{description}</p>
    </div>
  )
}

function DataCard({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string | number
  mono?: boolean
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background px-3 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className={mono ? 'mt-1 font-mono text-sm' : 'mt-1 text-sm font-medium'}>{value}</p>
    </div>
  )
}

function Badge({
  children,
  tone,
}: {
  children: ReactNode
  tone: 'slate' | 'blue' | 'amber'
}) {
  const toneClass =
    tone === 'amber'
      ? 'border-amber-300/70 bg-amber-50 text-amber-900'
      : tone === 'blue'
        ? 'border-sky-300/70 bg-sky-50 text-sky-900'
        : 'border-border/70 bg-background text-foreground'

  return (
    <span className={`rounded-full border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em] ${toneClass}`}>
      {children}
    </span>
  )
}

function SkeletonBlock({ className }: { className: string }) {
  return <div className={`animate-pulse rounded-2xl bg-muted/50 ${className}`} />
}

function parseMetadata(metadata: string | undefined): MetadataRecord {
  if (!metadata) {
    return {}
  }

  try {
    const parsed = JSON.parse(metadata) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as MetadataRecord)
      : {}
  } catch {
    return {}
  }
}

function formatValue(value: unknown): string {
  if (typeof value === 'string') {
    return value
  }

  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }

  if (value === null || value === undefined) {
    return '-'
  }

  return JSON.stringify(value)
}

function startCase(value: string): string {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, (match) => match.toUpperCase())
}

function looksLikeId(value: string): boolean {
  return value.length > 24 || value.includes('-')
}

function readString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null
}

function readNumber(value: unknown): number | null {
  return typeof value === 'number' ? value : null
}

function readBoolean(value: unknown): boolean {
  return value === true
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
