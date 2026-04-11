import { Link } from '@tanstack/react-router'
import {
  ArrowRightIcon,
  MonitorIcon,
  ShieldCheckIcon,
  UserIcon,
  UsersIcon,
} from 'lucide-react'
import type { DeviceDetail } from '@/api/devices.schemas'
import { getDefaultDescription } from '@/components/features/devices/DeviceDetailHelpers'
import { Badge, SkeletonBlock } from '@/components/features/devices/DeviceDetailShared'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { InsetPanel } from '@/components/ui/inset-panel'

// Phase 1 canonical cleanup (Task 15): device-native detail pane.
// Vulnerability list, software inventory and software CPE binding
// sections were removed alongside the legacy AssetDetail shape —
// Phase 5 will restore them once vulnerability tables are rewired off
// the Asset navigation to the canonical Device identity.

type DeviceDetailPaneProps = {
  device: DeviceDetail | null
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

export function DeviceDetailPane({
  device,
  isLoading,
  isOpen,
  onOpenChange,
}: DeviceDetailPaneProps) {
  return (
    <Sheet open={isOpen} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-card p-0 sm:max-w-md">
        <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
          <SheetTitle>{device?.name ?? 'Device summary'}</SheetTitle>
          <SheetDescription>Quick context and metrics overview.</SheetDescription>
        </SheetHeader>

        <div className="space-y-4 p-4">
          {isLoading ? (
            <div className="space-y-3">
              <SkeletonBlock className="h-20" />
              <SkeletonBlock className="h-32" />
            </div>
          ) : null}

          {!isLoading && !device ? (
            <InsetPanel emphasis="subtle" className="border-dashed p-6 text-sm text-muted-foreground">
              Select a device to inspect.
            </InsetPanel>
          ) : null}

          {!isLoading && device ? (
            <>
              <section className="space-y-3">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone="slate">Device</Badge>
                  <Badge tone={device.criticality === 'Critical' ? 'amber' : 'blue'}>
                    {device.criticality}
                  </Badge>
                  {device.securityProfile ? (
                    <Badge tone="blue">{device.securityProfile.name}</Badge>
                  ) : null}
                </div>
                <p className="text-sm text-muted-foreground">
                  {device.description ?? getDefaultDescription()}
                </p>
                <code className="block text-xs text-muted-foreground">{device.externalId}</code>
              </section>

              <section className="rounded-xl border border-border/70 bg-muted/30 p-3">
                <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  <MonitorIcon className="size-3.5" />
                  Device context
                </div>
                <div className="grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm">
                  {device.computerDnsName ? (
                    <>
                      <span className="text-muted-foreground">DNS name</span>
                      <span className="truncate font-medium">{device.computerDnsName}</span>
                    </>
                  ) : null}
                  {device.osPlatform ? (
                    <>
                      <span className="text-muted-foreground">OS</span>
                      <span className="font-medium">{device.osPlatform}{device.osVersion ? ` ${device.osVersion}` : ''}</span>
                    </>
                  ) : null}
                  {device.healthStatus ? (
                    <>
                      <span className="text-muted-foreground">Health</span>
                      <span className="font-medium">{device.healthStatus}</span>
                    </>
                  ) : null}
                  {device.riskScore ? (
                    <>
                      <span className="text-muted-foreground">Risk score</span>
                      <span className="font-medium">{device.riskScore}</span>
                    </>
                  ) : null}
                  {device.exposureLevel ? (
                    <>
                      <span className="text-muted-foreground">Exposure</span>
                      <span className="font-medium">{device.exposureLevel}</span>
                    </>
                  ) : null}
                  {device.lastSeenAt ? (
                    <>
                      <span className="text-muted-foreground">Last seen</span>
                      <span className="font-medium">{new Date(device.lastSeenAt).toLocaleDateString()}</span>
                    </>
                  ) : null}
                  {device.lastIpAddress ? (
                    <>
                      <span className="text-muted-foreground">IP address</span>
                      <span className="truncate font-mono text-xs font-medium">{device.lastIpAddress}</span>
                    </>
                  ) : null}
                  {device.groupName ? (
                    <>
                      <span className="text-muted-foreground">Group</span>
                      <span className="truncate font-medium">{device.groupName}</span>
                    </>
                  ) : null}
                </div>
              </section>

              <section className="rounded-xl border border-border/70 bg-muted/30 p-3">
                <div className="mb-2 flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  {device.ownerType === 'Team' ? <UsersIcon className="size-3.5" /> : <UserIcon className="size-3.5" />}
                  Ownership
                </div>
                <p className="text-sm font-medium">
                  {device.ownerType === 'Team'
                    ? device.ownerTeamName ?? 'Unassigned'
                    : device.ownerUserName ?? 'Unassigned'}
                </p>
              </section>

              {device.risk ? (
                <section className="space-y-2">
                  <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                    <ShieldCheckIcon className="size-3.5" />
                    Current risk
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    <MetricCard
                      label="Device risk"
                      value={device.risk.overallScore.toFixed(0)}
                      tone={device.risk.riskBand === 'Critical' ? 'danger' : device.risk.riskBand === 'High' ? 'warning' : 'default'}
                    />
                    <MetricCard
                      label="Max episode"
                      value={device.risk.maxEpisodeRiskScore.toFixed(0)}
                      tone={device.risk.maxEpisodeRiskScore >= 900 ? 'danger' : device.risk.maxEpisodeRiskScore >= 750 ? 'warning' : 'default'}
                    />
                    <MetricCard
                      label="Open episodes"
                      value={device.risk.openEpisodeCount}
                      tone={device.risk.openEpisodeCount > 0 ? 'danger' : 'success'}
                    />
                    <MetricCard
                      label="Top bands"
                      value={`${device.risk.criticalCount}/${device.risk.highCount}`}
                      tone={device.risk.criticalCount > 0 ? 'danger' : device.risk.highCount > 0 ? 'warning' : 'default'}
                    />
                  </div>
                </section>
              ) : null}

              <div className="flex gap-2">
                <Link
                  to="/devices/$id"
                  params={{ id: device.id }}
                  className="flex flex-1 items-center justify-center gap-2 rounded-xl border border-border/70 bg-background px-4 py-2.5 text-sm font-medium transition hover:bg-muted/30"
                >
                  Open full detail view
                  <ArrowRightIcon className="size-4" />
                </Link>
              </div>
            </>
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
  )
}
