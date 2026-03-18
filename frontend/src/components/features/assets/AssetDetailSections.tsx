import { useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import type { AssetDetail } from '@/api/assets.schemas'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  type MetadataRecord,
  readBoolean,
  readNumber,
  readString,
} from '@/components/features/assets/AssetDetailHelpers'
import {
  DataCard,
  KeyValueGrid,
  SectionHeader,
} from '@/components/features/assets/AssetDetailShared'
import { formatDate, formatDateTime } from '@/lib/formatting'
import { toneBadge, toneDot, type Tone } from '@/lib/tone-classes'

export function DeviceSecurityProfileSection({
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
        <Select
          value={asset.securityProfile?.id ?? ''}
          onValueChange={(value) => {
            onAssignSecurityProfile(asset.id, value || null)
          }}
          disabled={isAssigningSecurityProfile}
        >
          <SelectTrigger className="h-10 w-full rounded-md bg-background px-3">
            <SelectValue placeholder="No security profile" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="">No security profile</SelectItem>
            {securityProfiles.map((profile) => (
              <SelectItem key={profile.id} value={profile.id}>
                {profile.name} • {profile.internetReachability}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
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

export function SoftwareSection({
  asset,
  metadata,
  isAssigningSoftwareCpeBinding,
  onAssignSoftwareCpeBinding,
}: {
  asset: AssetDetail
  metadata: MetadataRecord
  isAssigningSoftwareCpeBinding: boolean
  onAssignSoftwareCpeBinding: (assetId: string, cpe23Uri: string | null) => void
}) {
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
                    <span className={`rounded-full border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em] ${toneBadge('success')}`}>
                      {item.matchMethod}
                    </span>
                    <span className={`rounded-full border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em] ${toneBadge('info')}`}>
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

export function DeviceSection({
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
    { label: 'Last Seen', value: asset.deviceLastSeenAt ? formatDateTime(asset.deviceLastSeenAt) : 'Unknown' },
    { label: 'Last IP Address', value: asset.deviceLastIpAddress ?? 'Unknown' },
    { label: 'Device Group', value: asset.deviceGroupName ?? 'Unknown' },
    { label: 'Exposure Level', value: asset.deviceExposureLevel ?? 'Unknown' },
    { label: 'AAD Joined', value: asset.deviceIsAadJoined === true ? 'Yes' : asset.deviceIsAadJoined === false ? 'No' : 'Unknown' },
    { label: 'Onboarding Status', value: asset.deviceOnboardingStatus ?? 'Unknown' },
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
      {asset.tags && asset.tags.length > 0 && (
        <div className="mt-3">
          <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground mb-1">Tags</p>
          <div className="flex flex-wrap gap-1">
            {asset.tags.map((tag) => (
              <Badge key={tag} variant="secondary" className="text-xs">{tag}</Badge>
            ))}
          </div>
        </div>
      )}
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
                  <p className="text-xs text-muted-foreground">Last seen {formatDate(software.lastSeenAt)}</p>
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

export function DeviceActivityTimeline({ asset }: { asset: AssetDetail }) {
  const items = useMemo<DeviceActivityItem[]>(() => {
    const vulnerabilityEvents = asset.vulnerabilities.flatMap((vulnerability) =>
      vulnerability.episodes.flatMap((episode) => {
        const events: DeviceActivityItem[] = [
          {
            id: `vuln:${vulnerability.vulnerabilityId}:start:${episode.episodeNumber}`,
            at: episode.firstSeenAt,
            title: `${vulnerability.externalId} detected`,
            detail: `${vulnerability.title} appeared on this device as episode #${episode.episodeNumber}.`,
            tone: episode.episodeNumber > 1 ? 'warning' : 'info',
          },
        ]

        if (episode.resolvedAt) {
          events.push({
            id: `vuln:${vulnerability.vulnerabilityId}:end:${episode.episodeNumber}`,
            at: episode.resolvedAt,
            title: `${vulnerability.externalId} resolved`,
            detail: `${vulnerability.title} was no longer detected on this device.`,
            tone: 'neutral',
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
            tone: episode.episodeNumber > 1 ? 'warning' : 'info',
          },
        ]

        if (episode.removedAt) {
          events.push({
            id: `software:${software.softwareAssetId}:end:${episode.episodeNumber}`,
            at: episode.removedAt,
            title: `${software.name} removed`,
            detail: `${software.externalId} was no longer present on the device.`,
            tone: 'neutral',
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
                className={`mt-1 h-2.5 w-2.5 rounded-full ${toneDot(item.tone)}`}
              />
              {index < items.length - 1 ? <span className="mt-1 h-full w-px bg-border/80" /> : null}
            </div>
            <div className="flex-1 rounded-xl border border-border/70 bg-background px-3 py-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-sm font-medium">{item.title}</p>
                <span className="text-xs text-muted-foreground">{formatDateTime(item.at)}</span>
              </div>
              <p className="mt-1 text-sm text-muted-foreground">{item.detail}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  )
}

export function GenericMetadataSection({ metadata }: { metadata: MetadataRecord }) {
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

type DeviceActivityItem = {
  id: string
  at: string
  title: string
  detail: string
  tone: Tone
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
        <DataCard label="Last Validated" value={formatDateTime(binding.lastValidatedAt)} />
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
        <Input
          className="bg-card"
          placeholder="cpe:2.3:a:vendor:product:version:*:*:*:*:*:*:*"
          value={value}
          onChange={(event) => onValueChange(event.target.value)}
          disabled={isSaving}
        />
        <div className="flex gap-2">
          <Button
            type="button"
            variant="outline"
            className="rounded-full bg-card"
            onClick={onSave}
            disabled={isSaving}
          >
            {isSaving ? 'Saving…' : 'Save binding'}
          </Button>
          <Button
            type="button"
            variant="outline"
            className="rounded-full bg-background text-muted-foreground"
            onClick={() => onValueChange('')}
            disabled={isSaving}
          >
            Clear value
          </Button>
        </div>
      </div>
    </div>
  )
}
