import type { DeviceDetail } from '@/api/devices.schemas'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import { Badge } from '@/components/ui/badge'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  type MetadataRecord,
} from '@/components/features/devices/DeviceDetailHelpers'
import {
  DataCard,
  KeyValueGrid,
  SectionHeader,
} from '@/components/features/devices/DeviceDetailShared'
import { formatDateTime } from '@/lib/formatting'

// Phase 1 canonical cleanup (Task 15): device-native detail sections.
// SoftwareSection, DeviceActivityTimeline, and the CPE binding editor
// have been removed alongside the legacy AssetDetail fields
// (softwareCpeBinding, softwareInventory, vulnerabilities[],
// knownSoftwareVulnerabilities). Phase 5 will reintroduce the software
// and vulnerability timelines once those tables are rewired to the
// canonical Device identity.

export function DeviceSecurityProfileSection({
  device,
  securityProfiles,
  isAssigningSecurityProfile,
  onAssignSecurityProfile,
}: {
  device: DeviceDetail
  securityProfiles: SecurityProfile[]
  isAssigningSecurityProfile: boolean
  onAssignSecurityProfile: (deviceId: string, securityProfileId: string | null) => void
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
          value={device.securityProfile?.id ?? ''}
          onValueChange={(value) => {
            onAssignSecurityProfile(device.id, value || null)
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
          {isAssigningSecurityProfile ? 'Applying profile...' : device.securityProfile?.name ?? 'Using vendor severity only'}
        </div>
      </div>
      {device.securityProfile ? (
        <div className="mt-3 grid gap-3 md:grid-cols-2">
          <DataCard label="Environment Class" value={device.securityProfile.environmentClass} />
          <DataCard label="Reachability" value={device.securityProfile.internetReachability} />
          <DataCard label="Confidentiality Requirement" value={device.securityProfile.confidentialityRequirement} />
          <DataCard label="Integrity Requirement" value={device.securityProfile.integrityRequirement} />
          <DataCard label="Availability Requirement" value={device.securityProfile.availabilityRequirement} />
        </div>
      ) : null}
    </section>
  )
}

export function DeviceSection({
  device,
  metadata,
}: {
  device: DeviceDetail
  metadata: MetadataRecord
}) {
  const normalizedFields = [
    {
      label: "Machine Name",
      value: device.computerDnsName ?? device.name ?? "Unknown",
    },
    { label: "Health Status", value: device.healthStatus ?? "Unknown" },
    { label: "OS Platform", value: device.osPlatform ?? "Unknown" },
    { label: "OS Version", value: device.osVersion ?? "Unknown" },
    { label: "Risk Score", value: device.riskScore ?? "Unknown" },
    {
      label: "Last Seen",
      value: device.lastSeenAt
        ? formatDateTime(device.lastSeenAt)
        : "Unknown",
    },
    { label: "Last IP Address", value: device.lastIpAddress ?? "Unknown" },
    { label: "Device Group", value: device.groupName ?? "Unknown" },
    { label: "Exposure Level", value: device.exposureLevel ?? "Unknown" },
    {
      label: "AAD Joined",
      value:
        device.isAadJoined === true
          ? "Yes"
          : device.isAadJoined === false
            ? "No"
            : "Unknown",
    },
    {
      label: "Onboarding Status",
      value: device.onboardingStatus ?? "Unknown",
    },
    { label: "Device Value", value: device.deviceValue ?? "Unknown" },
    {
      label: "Entra Device ID",
      value: device.aadDeviceId ?? "Unknown",
      mono: true,
    },
  ];

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
      {device.tags && device.tags.length > 0 && (
        <div className="mt-3">
          <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground mb-1">Tags</p>
          <div className="flex flex-wrap gap-1">
            {device.tags.map((tag) => (
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
            description="Any remaining device-specific data that has not been normalized yet."
          />
          <KeyValueGrid metadata={metadata} />
        </div>
      ) : null}
    </section>
  )
}

export function GenericMetadataSection({ metadata }: { metadata: MetadataRecord }) {
  return (
    <section className="rounded-2xl border border-border/70 bg-card p-4">
      <SectionHeader
        eyebrow="Metadata"
        title="Stored context"
        description="Device-specific data persisted on the device record."
      />
      {Object.keys(metadata).length === 0 ? (
        <p className="text-sm text-muted-foreground">No additional metadata is stored for this device.</p>
      ) : (
        <KeyValueGrid metadata={metadata} />
      )}
    </section>
  )
}
