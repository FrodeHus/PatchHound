import { useMemo, type ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type { AssetDetail } from '@/api/assets.schemas'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'

type AssetDetailPaneProps = {
  asset: AssetDetail | null
  isLoading: boolean
  isOpen: boolean
  onOpenChange: (open: boolean) => void
}

type MetadataRecord = Record<string, unknown>

export function AssetDetailPane({
  asset,
  isLoading,
  isOpen,
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
                <DataCard label="Owner Type" value={asset.ownerType} />
                <DataCard label="Owner User" value={asset.ownerUserId ?? 'Unassigned'} mono />
                <DataCard label="Owner Team" value={asset.ownerTeamId ?? 'None'} mono />
                <DataCard label="Fallback Team" value={asset.fallbackTeamId ?? 'None'} mono />
              </section>

              {asset.assetType === 'Software' ? (
                <SoftwareSection metadata={metadata} />
              ) : asset.assetType === 'Device' ? (
                <DeviceSection metadata={metadata} />
              ) : (
                <GenericMetadataSection metadata={metadata} />
              )}

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
                            <p className="font-medium">{vulnerability.title}</p>
                            <p className="mt-1 text-xs text-muted-foreground">
                              {vulnerability.externalId} • {vulnerability.vendorSeverity} • {vulnerability.status}
                            </p>
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

function SoftwareSection({ metadata }: { metadata: MetadataRecord }) {
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
    </section>
  )
}

function DeviceSection({ metadata }: { metadata: MetadataRecord }) {
  return (
    <section className="rounded-2xl border border-border/70 bg-card p-4">
      <SectionHeader
        eyebrow="Device telemetry"
        title="Host context"
        description="Machine-specific metadata currently stored for this device asset."
      />
      {Object.keys(metadata).length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No device-specific metadata is currently stored for this record.
        </p>
      ) : (
        <KeyValueGrid metadata={metadata} />
      )}
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
