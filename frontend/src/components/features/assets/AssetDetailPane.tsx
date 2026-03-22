import { useMemo } from 'react'
import { Link } from '@tanstack/react-router'
import {
  ArrowRightIcon,
  MonitorIcon,
  ShieldAlertIcon,
  ShieldCheckIcon,
  ActivityIcon,
  UserIcon,
  UsersIcon,
  PackageIcon,
} from 'lucide-react'
import type { AssetDetail } from '@/api/assets.schemas'
import {
  getDefaultDescription,
} from '@/components/features/assets/AssetDetailHelpers'
import {
  Badge,
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
  isLoading: boolean
  isOpen: boolean
  onOpenChange: (open: boolean) => void
}

function MetricCard({
  label,
  value,
  tone = 'default',
}: {
  label: string
  value: string | number
  tone?: 'default' | 'danger' | 'warning' | 'success'
}) {
  const toneClasses = {
    default: 'border-border/70 bg-background',
    danger: 'border-tone-danger-border bg-tone-danger',
    warning: 'border-tone-warning-border bg-tone-warning',
    success: 'border-tone-success-border bg-tone-success',
  }

  const valueToneClasses = {
    default: 'text-foreground',
    danger: 'text-tone-danger-foreground',
    warning: 'text-tone-warning-foreground',
    success: 'text-tone-success-foreground',
  }

  return (
    <div className={`rounded-xl border px-3 py-2.5 ${toneClasses[tone]}`}>
      <p className="text-[10px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className={`mt-0.5 text-lg font-semibold tabular-nums ${valueToneClasses[tone]}`}>{value}</p>
    </div>
  )
}

export function AssetDetailPane({
  asset,
  isLoading,
  isOpen,
  onOpenChange,
}: AssetDetailPaneProps) {
  const openVulnCount = useMemo(
    () => asset?.vulnerabilities.filter((v) => v.status === 'Open').length ?? 0,
    [asset?.vulnerabilities],
  )
  const resolvedVulnCount = useMemo(
    () => asset?.vulnerabilities.filter((v) => v.status !== 'Open').length ?? 0,
    [asset?.vulnerabilities],
  )
  const recurringCount = useMemo(
    () => asset?.vulnerabilities.filter((v) => v.episodeCount > 1).length ?? 0,
    [asset?.vulnerabilities],
  )
  const criticalHighCount = useMemo(
    () =>
      asset?.vulnerabilities.filter(
        (v) =>
          v.status === 'Open' &&
          (v.effectiveSeverity === 'Critical' || v.effectiveSeverity === 'High'),
      ).length ?? 0,
    [asset?.vulnerabilities],
  )

  return (
    <Sheet open={isOpen} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-card p-0 sm:max-w-md">
        <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
          <SheetTitle>{asset?.name ?? 'Asset summary'}</SheetTitle>
          <SheetDescription>Quick context and metrics overview.</SheetDescription>
        </SheetHeader>

        <div className="space-y-4 p-4">
          {isLoading ? (
            <div className="space-y-3">
              <SkeletonBlock className="h-20" />
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
              {/* Identity */}
              <section className="space-y-3">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone="slate">{asset.assetType}</Badge>
                  <Badge tone={asset.criticality === 'Critical' ? 'amber' : 'blue'}>
                    {asset.criticality}
                  </Badge>
                  {asset.securityProfile ? (
                    <Badge tone="blue">{asset.securityProfile.name}</Badge>
                  ) : null}
                </div>
                <p className="text-sm text-muted-foreground">
                  {asset.description ?? getDefaultDescription(asset.assetType)}
                </p>
                <code className="block text-xs text-muted-foreground">{asset.externalId}</code>
              </section>

              {/* Device context */}
              {asset.assetType === 'Device' ? (
                <section className="rounded-xl border border-border/70 bg-muted/30 p-3">
                  <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                    <MonitorIcon className="size-3.5" />
                    Device context
                  </div>
                  <div className="grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm">
                    {asset.deviceComputerDnsName ? (
                      <>
                        <span className="text-muted-foreground">DNS name</span>
                        <span className="truncate font-medium">{asset.deviceComputerDnsName}</span>
                      </>
                    ) : null}
                    {asset.deviceOsPlatform ? (
                      <>
                        <span className="text-muted-foreground">OS</span>
                        <span className="font-medium">{asset.deviceOsPlatform}{asset.deviceOsVersion ? ` ${asset.deviceOsVersion}` : ''}</span>
                      </>
                    ) : null}
                    {asset.deviceHealthStatus ? (
                      <>
                        <span className="text-muted-foreground">Health</span>
                        <span className="font-medium">{asset.deviceHealthStatus}</span>
                      </>
                    ) : null}
                    {asset.deviceRiskScore ? (
                      <>
                        <span className="text-muted-foreground">Risk score</span>
                        <span className="font-medium">{asset.deviceRiskScore}</span>
                      </>
                    ) : null}
                    {asset.deviceExposureLevel ? (
                      <>
                        <span className="text-muted-foreground">Exposure</span>
                        <span className="font-medium">{asset.deviceExposureLevel}</span>
                      </>
                    ) : null}
                    {asset.deviceLastSeenAt ? (
                      <>
                        <span className="text-muted-foreground">Last seen</span>
                        <span className="font-medium">{new Date(asset.deviceLastSeenAt).toLocaleDateString()}</span>
                      </>
                    ) : null}
                    {asset.deviceLastIpAddress ? (
                      <>
                        <span className="text-muted-foreground">IP address</span>
                        <span className="truncate font-mono text-xs font-medium">{asset.deviceLastIpAddress}</span>
                      </>
                    ) : null}
                    {asset.deviceGroupName ? (
                      <>
                        <span className="text-muted-foreground">Group</span>
                        <span className="truncate font-medium">{asset.deviceGroupName}</span>
                      </>
                    ) : null}
                  </div>
                </section>
              ) : null}

              {/* Software context */}
              {asset.assetType === 'Software' ? (
                <section className="rounded-xl border border-border/70 bg-muted/30 p-3">
                  <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                    <PackageIcon className="size-3.5" />
                    Software context
                  </div>
                  <div className="grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm">
                    {asset.softwareCpeBinding?.matchedVendor ? (
                      <>
                        <span className="text-muted-foreground">Vendor</span>
                        <span className="font-medium">{asset.softwareCpeBinding.matchedVendor}</span>
                      </>
                    ) : null}
                    {asset.softwareCpeBinding?.matchedVersion ? (
                      <>
                        <span className="text-muted-foreground">Version</span>
                        <span className="font-mono text-xs font-medium">{asset.softwareCpeBinding.matchedVersion}</span>
                      </>
                    ) : null}
                    {asset.softwareInventory.length > 0 ? (
                      <>
                        <span className="text-muted-foreground">Installations</span>
                        <span className="font-medium">{asset.softwareInventory.length}</span>
                      </>
                    ) : null}
                    {asset.knownSoftwareVulnerabilities.length > 0 ? (
                      <>
                        <span className="text-muted-foreground">Known vulns</span>
                        <span className="font-medium">{asset.knownSoftwareVulnerabilities.length}</span>
                      </>
                    ) : null}
                  </div>
                </section>
              ) : null}

              {/* Ownership */}
              <section className="rounded-xl border border-border/70 bg-muted/30 p-3">
                <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  {asset.ownerType === 'Team' ? <UsersIcon className="size-3.5" /> : <UserIcon className="size-3.5" />}
                  Ownership
                </div>
                <p className="text-sm font-medium">
                  {asset.ownerType === 'Team'
                    ? asset.ownerTeamId ?? 'Unassigned'
                    : asset.ownerUserId ?? 'Unassigned'}
                </p>
              </section>

              {/* Vulnerability metrics */}
              {asset.risk ? (
                <section className="space-y-2">
                  <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                    <ShieldCheckIcon className="size-3.5" />
                    Current risk
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    <MetricCard
                      label="Asset risk"
                      value={asset.risk.overallScore.toFixed(0)}
                      tone={asset.risk.riskBand === 'Critical' ? 'danger' : asset.risk.riskBand === 'High' ? 'warning' : 'default'}
                    />
                    <MetricCard
                      label="Max episode"
                      value={asset.risk.maxEpisodeRiskScore.toFixed(0)}
                      tone={asset.risk.maxEpisodeRiskScore >= 900 ? 'danger' : asset.risk.maxEpisodeRiskScore >= 750 ? 'warning' : 'default'}
                    />
                    <MetricCard
                      label="Open episodes"
                      value={asset.risk.openEpisodeCount}
                      tone={asset.risk.openEpisodeCount > 0 ? 'danger' : 'success'}
                    />
                    <MetricCard
                      label="Top bands"
                      value={`${asset.risk.criticalCount}/${asset.risk.highCount}`}
                      tone={asset.risk.criticalCount > 0 ? 'danger' : asset.risk.highCount > 0 ? 'warning' : 'default'}
                    />
                  </div>
                </section>
              ) : null}

              {/* Vulnerability metrics */}
              <section className="space-y-2">
                <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  <ShieldAlertIcon className="size-3.5" />
                  Vulnerability metrics
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <MetricCard
                    label="Open"
                    value={openVulnCount}
                    tone={openVulnCount > 0 ? 'danger' : 'success'}
                  />
                  <MetricCard
                    label="Critical / High"
                    value={criticalHighCount}
                    tone={criticalHighCount > 0 ? 'danger' : 'default'}
                  />
                  <MetricCard
                    label="Recurring"
                    value={recurringCount}
                    tone={recurringCount > 0 ? 'warning' : 'default'}
                  />
                  <MetricCard
                    label="Resolved"
                    value={resolvedVulnCount}
                    tone={resolvedVulnCount > 0 ? 'success' : 'default'}
                  />
                </div>
              </section>

              {/* Top vulnerabilities (compact list, max 5) */}
              {asset.vulnerabilities.length > 0 ? (
                <section className="space-y-2">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                      <ActivityIcon className="size-3.5" />
                      Recent vulnerabilities
                    </div>
                    <span className="text-xs text-muted-foreground">
                      {asset.vulnerabilities.length} total
                    </span>
                  </div>
                  <div className="space-y-1">
                    {asset.vulnerabilities.slice(0, 5).map((vuln) => (
                      <Link
                        key={vuln.vulnerabilityId}
                        to="/vulnerabilities/$id"
                        params={{ id: vuln.vulnerabilityId }}
                        className="flex items-center justify-between gap-2 rounded-lg border border-border/60 bg-background px-3 py-2 transition hover:border-foreground/20 hover:bg-muted/50"
                      >
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-sm font-medium">{vuln.title}</p>
                          <p className="text-xs text-muted-foreground">
                            {vuln.externalId} · {vuln.effectiveSeverity ?? vuln.vendorSeverity}
                            {vuln.episodeCount > 1 ? ` · ${vuln.episodeCount - 1}x recurred` : ''}
                          </p>
                        </div>
                        <span
                          className={`shrink-0 rounded-full border px-2 py-0.5 text-[10px] font-medium uppercase tracking-wide ${
                            vuln.status === 'Open'
                              ? 'border-tone-danger-border bg-tone-danger text-tone-danger-foreground'
                              : 'border-tone-success-border bg-tone-success text-tone-success-foreground'
                          }`}
                        >
                          {vuln.status}
                        </span>
                      </Link>
                    ))}
                  </div>
                  {asset.vulnerabilities.length > 5 ? (
                    <p className="text-center text-xs text-muted-foreground">
                      +{asset.vulnerabilities.length - 5} more — open detail view to see all
                    </p>
                  ) : null}
                </section>
              ) : (
                <section className="rounded-xl border border-border/60 bg-background p-3">
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <ShieldCheckIcon className="size-4 text-tone-success-foreground" />
                    No vulnerabilities linked to this asset.
                  </div>
                </section>
              )}

              {/* Detail link */}
              <div className="flex gap-2">
                <Link
                  to="/assets/$id"
                  params={{ id: asset.id }}
                  className="flex flex-1 items-center justify-center gap-2 rounded-xl border border-border/70 bg-background px-4 py-2.5 text-sm font-medium transition hover:bg-muted/30"
                >
                  Open full detail view
                  <ArrowRightIcon className="size-4" />
                </Link>
                {asset.assetType === 'Software' && asset.tenantSoftwareId ? (
                  <Link
                    to="/software/$id"
                    params={{ id: asset.tenantSoftwareId }}
                    search={{ page: 1, pageSize: 25, version: '' }}
                    className="flex items-center justify-center gap-2 rounded-xl border border-primary/30 bg-primary/10 px-4 py-2.5 text-sm font-medium text-primary transition hover:bg-primary/15"
                  >
                    Software workspace
                  </Link>
                ) : null}
              </div>
            </>
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
  )
}
