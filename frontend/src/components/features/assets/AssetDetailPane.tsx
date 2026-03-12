import { useMemo } from 'react'
import { Link } from '@tanstack/react-router'
import type { AssetDetail } from '@/api/assets.schemas'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import {
  getDefaultDescription,
  parseMetadata,
} from '@/components/features/assets/AssetDetailHelpers'
import {
  DeviceActivityTimeline,
  DeviceSection,
  DeviceSecurityProfileSection,
  GenericMetadataSection,
  SoftwareSection,
} from '@/components/features/assets/AssetDetailSections'
import {
  Badge,
  DataCard,
  SkeletonBlock,
} from '@/components/features/assets/AssetDetailShared'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { InsetPanel } from '@/components/ui/inset-panel'

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
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-card p-0 sm:max-w-2xl">
        <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
          <SheetTitle>{asset?.name ?? 'Asset detail'}</SheetTitle>
          <SheetDescription>
            Inspect operational context, ownership, and type-specific signals without leaving the asset table.
          </SheetDescription>
        </SheetHeader>

        <div className="space-y-6 p-5">
          {isLoading ? (
            <div className="space-y-3">
              <SkeletonBlock className="h-24" />
              <SkeletonBlock className="h-40" />
              <SkeletonBlock className="h-32" />
            </div>
          ) : null}

          {!isLoading && !asset ? (
            <InsetPanel emphasis="subtle" className="border-dashed p-6 text-sm text-muted-foreground">
              Select an asset to inspect.
            </InsetPanel>
          ) : null}

          {!isLoading && asset ? (
            <>
              <section className="rounded-2xl border border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_94%,black),var(--card))] p-4 shadow-sm">
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
                    <div className="flex flex-wrap gap-2">
                      <Link
                        to="/assets/$id"
                        params={{ id: asset.id }}
                        className="inline-flex rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm font-medium text-foreground hover:bg-muted/20"
                      >
                        Open detail view
                      </Link>
                      {asset.assetType === 'Software' && asset.tenantSoftwareId ? (
                        <Link
                          to="/software/$id"
                          params={{ id: asset.tenantSoftwareId }}
                          search={{ page: 1, pageSize: 25, version: '' }}
                          className="inline-flex rounded-full border border-primary/30 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/15"
                        >
                          Open software workspace
                        </Link>
                      ) : null}
                    </div>
                  </div>
                  <InsetPanel emphasis="strong" className="px-3 py-2 text-right">
                    <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                      External ID
                    </p>
                    <code className="text-xs">{asset.externalId}</code>
                  </InsetPanel>
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
                <SoftwareSection
                  asset={asset}
                  metadata={metadata}
                  isAssigningSoftwareCpeBinding={isAssigningSoftwareCpeBinding}
                  onAssignSoftwareCpeBinding={onAssignSoftwareCpeBinding}
                />
              ) : asset.assetType === 'Device' ? (
                <DeviceSection asset={asset} metadata={metadata} />
              ) : (
                <GenericMetadataSection metadata={metadata} />
              )}

              {asset.assetType === 'Device' ? <DeviceActivityTimeline asset={asset} /> : null}

              <section className="rounded-2xl border border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_94%,black),var(--card))] p-4">
                <div className="mb-4 flex items-end justify-between gap-3">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                      Exposure
                    </p>
                    <h3 className="text-lg font-semibold">Linked vulnerabilities</h3>
                  </div>
                  <InsetPanel emphasis="subtle" className="px-3 py-1 text-sm">
                    {asset.vulnerabilities.length} linked
                  </InsetPanel>
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
                        className="block rounded-xl border border-border/80 bg-muted/55 px-3 py-3 transition hover:border-foreground/20 hover:bg-muted/70"
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
