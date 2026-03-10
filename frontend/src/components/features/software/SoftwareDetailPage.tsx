import type { ReactNode } from 'react'
import { Link } from '@tanstack/react-router'
import type {
  NormalizedSoftwareDetail,
  NormalizedSoftwareVulnerability,
  PagedNormalizedSoftwareInstallations,
} from '@/api/software.schemas'
import { formatDate, formatDateTime, startCase } from '@/lib/formatting'

type SoftwareDetailPageProps = {
  detail: NormalizedSoftwareDetail
  selectedVersion: string
  installations: PagedNormalizedSoftwareInstallations
  vulnerabilities: NormalizedSoftwareVulnerability[]
  onSelectVersion: (version: string) => void
  onPageChange: (page: number) => void
}

export function SoftwareDetailPage({
  detail,
  selectedVersion,
  installations,
  vulnerabilities,
  onSelectVersion,
  onPageChange,
}: SoftwareDetailPageProps) {
  const activeVersion =
    detail.versionCohorts.find((cohort) => normalizeVersion(cohort.version) === selectedVersion) ??
    detail.versionCohorts[0] ??
    null

  return (
    <section className="space-y-5">
      <header className="overflow-hidden rounded-[32px] border border-border/70 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--primary)_12%,transparent),transparent_45%),linear-gradient(180deg,color-mix(in_oklab,var(--foreground)_4%,transparent),transparent_60%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-3">
            <Link to="/" className="text-sm text-muted-foreground hover:text-foreground">
              Workspace
            </Link>
            <div className="flex flex-wrap gap-2">
              <Pill>{detail.normalizationMethod}</Pill>
              <Pill>{detail.confidence} confidence</Pill>
              {detail.primaryCpe23Uri ? <Pill>CPE bound</Pill> : <Pill>Heuristic identity</Pill>}
            </div>
            <div>
              <h1 className="text-3xl font-semibold tracking-[-0.04em]">{startCase(detail.canonicalName)}</h1>
              <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
                {detail.canonicalVendor ? `${startCase(detail.canonicalVendor)} normalized product` : 'Normalized software identity'}
                {' '}spanning {detail.versionCount} version cohort{detail.versionCount === 1 ? '' : 's'} and {detail.activeInstallCount} active install{detail.activeInstallCount === 1 ? '' : 's'} in the current tenant.
              </p>
            </div>
          </div>
          <div className="grid min-w-[220px] gap-3 rounded-3xl border border-border/70 bg-background/50 p-4">
            <Metric label="Active installs" value={String(detail.activeInstallCount)} />
            <Metric label="Devices" value={String(detail.uniqueDeviceCount)} />
            <Metric label="Vulnerable installs" value={String(detail.vulnerableInstallCount)} tone={detail.vulnerableInstallCount > 0 ? 'danger' : 'neutral'} />
            <Metric label="Open vulnerabilities" value={String(detail.activeVulnerabilityCount)} tone={detail.activeVulnerabilityCount > 0 ? 'warning' : 'neutral'} />
          </div>
        </div>
      </header>

      <section className="rounded-[28px] border border-border/70 bg-card p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Version pressure rail</h2>
            <p className="text-sm text-muted-foreground">Select a cohort to page through installs and focus the detail view.</p>
          </div>
          {activeVersion ? (
            <div className="text-sm text-muted-foreground">
              Focused cohort: <span className="font-medium text-foreground">{formatVersion(activeVersion.version)}</span>
            </div>
          ) : null}
        </div>
        <div className="mt-5 grid gap-3 lg:grid-cols-4">
          {detail.versionCohorts.map((cohort) => {
            const versionKey = normalizeVersion(cohort.version)
            const isSelected = versionKey === selectedVersion
            return (
              <button
                key={versionKey || '__unknown__'}
                type="button"
                onClick={() => onSelectVersion(versionKey)}
                className={isSelected
                  ? 'rounded-3xl border border-primary/30 bg-primary/10 p-4 text-left'
                  : 'rounded-3xl border border-border/70 bg-background p-4 text-left hover:border-foreground/20 hover:bg-muted/20'}
              >
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-sm font-medium">{formatVersion(cohort.version)}</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {cohort.activeInstallCount} installs on {cohort.deviceCount} devices
                    </p>
                  </div>
                  <div className="rounded-full bg-background/80 px-2 py-1 text-xs text-muted-foreground">
                    {cohort.activeVulnerabilityCount} vuln{cohort.activeVulnerabilityCount === 1 ? '' : 's'}
                  </div>
                </div>
                <div className="mt-4 h-2 overflow-hidden rounded-full bg-muted">
                  <div
                    className={cohort.activeVulnerabilityCount > 0 ? 'h-full rounded-full bg-amber-500' : 'h-full rounded-full bg-emerald-500'}
                    style={{ width: `${Math.max(14, Math.min(100, (cohort.activeInstallCount / Math.max(detail.activeInstallCount, 1)) * 100))}%` }}
                  />
                </div>
                <p className="mt-3 text-xs text-muted-foreground">
                  Last seen {formatDate(cohort.lastSeenAt)}
                </p>
              </button>
            )
          })}
        </div>
      </section>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]">
        <div className="space-y-4">
          <section className="rounded-[28px] border border-border/70 bg-card p-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold">Cohort installations</h2>
                <p className="text-sm text-muted-foreground">Paged installations for the selected version cohort.</p>
              </div>
              {activeVersion ? (
                <div className="rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm">
                  {formatVersion(activeVersion.version)}
                </div>
              ) : null}
            </div>

            {activeVersion ? (
              <div className="mt-4 grid gap-3 sm:grid-cols-3">
                <Metric label="Installs" value={String(activeVersion.activeInstallCount)} />
                <Metric label="Devices" value={String(activeVersion.deviceCount)} />
                <Metric label="Open vulns" value={String(activeVersion.activeVulnerabilityCount)} tone={activeVersion.activeVulnerabilityCount > 0 ? 'warning' : 'neutral'} />
              </div>
            ) : null}

            {installations.items.length === 0 ? (
              <p className="mt-6 text-sm text-muted-foreground">No installations found for this cohort.</p>
            ) : (
              <div className="mt-5 overflow-hidden rounded-3xl border border-border/70">
                <table className="min-w-full divide-y divide-border/70 text-sm">
                  <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
                    <tr>
                      <th className="px-4 py-3">Device</th>
                      <th className="px-4 py-3">Criticality</th>
                      <th className="px-4 py-3">Profile</th>
                      <th className="px-4 py-3">Open vulns</th>
                      <th className="px-4 py-3">Last seen</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/60 bg-background">
                    {installations.items.map((item) => (
                      <tr key={`${item.deviceAssetId}-${item.softwareAssetId}`} className="align-top">
                        <td className="px-4 py-3">
                          <div>
                            <Link
                              to="/assets/$id"
                              params={{ id: item.deviceAssetId }}
                              className="font-medium hover:text-primary"
                            >
                              {item.deviceName}
                            </Link>
                            <p className="mt-1 text-xs text-muted-foreground">
                              {item.softwareAssetName} • episode #{item.currentEpisodeNumber}
                            </p>
                          </div>
                        </td>
                        <td className="px-4 py-3 text-muted-foreground">{item.deviceCriticality}</td>
                        <td className="px-4 py-3 text-muted-foreground">{item.securityProfileName ?? 'None'}</td>
                        <td className="px-4 py-3">
                          <span className={item.openVulnerabilityCount > 0 ? 'text-amber-700' : 'text-emerald-700'}>
                            {item.openVulnerabilityCount}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-muted-foreground">{formatDateTime(item.lastSeenAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="mt-4 flex items-center justify-between gap-3">
              <p className="text-sm text-muted-foreground">
                Page {installations.page} of {Math.max(installations.totalPages, 1)}
              </p>
              <div className="flex gap-2">
                <PageButton
                  disabled={installations.page <= 1}
                  onClick={() => onPageChange(installations.page - 1)}
                  label="Previous"
                />
                <PageButton
                  disabled={installations.page >= Math.max(installations.totalPages, 1)}
                  onClick={() => onPageChange(installations.page + 1)}
                  label="Next"
                />
              </div>
            </div>
          </section>

          <section className="rounded-[28px] border border-border/70 bg-card p-5">
            <div>
              <h2 className="text-lg font-semibold">Known vulnerabilities</h2>
              <p className="text-sm text-muted-foreground">Aggregated exposure for this normalized product across all linked software aliases.</p>
            </div>
            {vulnerabilities.length === 0 ? (
              <p className="mt-5 text-sm text-muted-foreground">No vulnerability matches are currently projected for this software.</p>
            ) : (
              <div className="mt-5 space-y-3">
                {vulnerabilities.map((item) => (
                  <Link
                    key={item.vulnerabilityId}
                    to="/vulnerabilities/$id"
                    params={{ id: item.vulnerabilityId }}
                    className="block rounded-3xl border border-border/70 bg-background p-4 hover:border-foreground/20 hover:bg-muted/20"
                  >
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="space-y-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <p className="font-medium">{item.title}</p>
                          <Pill>{item.vendorSeverity}</Pill>
                          {item.bestConfidence ? <Pill>{item.bestConfidence}</Pill> : null}
                        </div>
                        <p className="text-xs text-muted-foreground">
                          {item.externalId} • {item.source}
                          {item.cvssScore !== null ? ` • CVSS ${item.cvssScore.toFixed(1)}` : ''}
                        </p>
                        <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
                          <span>{item.affectedInstallCount} installs</span>
                          <span>{item.affectedDeviceCount} devices</span>
                          <span>{item.affectedVersionCount} versions</span>
                        </div>
                        <div className="flex flex-wrap gap-1">
                          {item.affectedVersions.map((version) => (
                            <span
                              key={version}
                              className={normalizeVersion(version) === selectedVersion
                                ? 'rounded-full border border-primary/30 bg-primary/10 px-2 py-1 text-xs text-primary'
                                : 'rounded-full border border-border/70 bg-card px-2 py-1 text-xs text-muted-foreground'}
                            >
                              {version}
                            </span>
                          ))}
                        </div>
                        {item.evidence[0] ? (
                          <p className="text-xs text-muted-foreground">{item.evidence[0].evidence}</p>
                        ) : null}
                      </div>
                      <div className="text-right text-xs text-muted-foreground">
                        <p>{item.bestMatchMethod}</p>
                        <p className="mt-1">Seen {formatDate(item.lastSeenAt)}</p>
                      </div>
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </section>
        </div>

        <aside className="space-y-4">
          <section className="rounded-[28px] border border-border/70 bg-card p-5">
            <h2 className="text-lg font-semibold">Identity rail</h2>
            <div className="mt-4 grid gap-3">
              <Metric label="Canonical vendor" value={detail.canonicalVendor ? startCase(detail.canonicalVendor) : 'Unknown'} />
              <Metric label="First seen" value={detail.firstSeenAt ? formatDate(detail.firstSeenAt) : 'Unknown'} />
              <Metric label="Last seen" value={detail.lastSeenAt ? formatDate(detail.lastSeenAt) : 'Unknown'} />
              <Metric label="Primary CPE" value={detail.primaryCpe23Uri ?? 'Not bound'} mono />
            </div>
          </section>

          <section className="rounded-[28px] border border-border/70 bg-card p-5">
            <h2 className="text-lg font-semibold">Source identities</h2>
            <div className="mt-4 space-y-3">
              {detail.sourceAliases.map((alias) => (
                <div key={`${alias.sourceSystem}-${alias.externalSoftwareId}`} className="rounded-2xl border border-border/70 bg-background p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="font-medium">{alias.rawName}</p>
                    <Pill>{alias.sourceSystem}</Pill>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">{alias.externalSoftwareId}</p>
                  <div className="mt-3 grid gap-2">
                    <Metric label="Vendor" value={alias.rawVendor ?? 'Unknown'} />
                    <Metric label="Version" value={alias.rawVersion ?? 'Unknown'} />
                    <Metric label="Match" value={alias.matchReason} />
                  </div>
                </div>
              ))}
            </div>
          </section>
        </aside>
      </div>
    </section>
  )
}

function Metric({
  label,
  value,
  mono = false,
  tone = 'neutral',
}: {
  label: string
  value: string
  mono?: boolean
  tone?: 'neutral' | 'warning' | 'danger'
}) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
      <p className={`mt-2 text-sm ${mono ? 'font-mono text-xs break-all' : 'font-medium'} ${
        tone === 'warning' ? 'text-amber-700' : tone === 'danger' ? 'text-rose-700' : 'text-foreground'
      }`}>
        {value}
      </p>
    </div>
  )
}

function Pill({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-full border border-border/70 bg-background px-3 py-1 text-xs text-muted-foreground">
      {children}
    </span>
  )
}

function PageButton({
  disabled,
  label,
  onClick,
}: {
  disabled: boolean
  label: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="rounded-full border border-border/70 bg-background px-3 py-1.5 text-sm text-muted-foreground transition hover:bg-muted/20 disabled:cursor-not-allowed disabled:opacity-50"
    >
      {label}
    </button>
  )
}

function normalizeVersion(version: string | null) {
  return version?.trim() ?? ''
}

function formatVersion(version: string | null) {
  return version && version.trim().length > 0 ? version : 'Unknown version'
}
