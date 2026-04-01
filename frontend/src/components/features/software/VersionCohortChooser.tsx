import { useMemo, useState } from 'react'
import { toneBadge } from '@/lib/tone-classes'
import { formatDate } from '@/lib/formatting'

type VersionCohortLike = {
  version: string | null
  activeInstallCount: number
  deviceCount: number
  activeVulnerabilityCount: number
  lastSeenAt: string
}

type VersionCohortChooserProps = {
  title: string
  description: string
  cohorts: VersionCohortLike[]
  selectedVersion: string
  onSelectVersion: (version: string) => void
  formatVersion: (version: string | null) => string
  normalizeVersion: (version: string | null) => string
  emptyText?: string
}

type CohortSort = 'vulnerable' | 'recent' | 'version'

export function VersionCohortChooser({
  title,
  description,
  cohorts,
  selectedVersion,
  onSelectVersion,
  formatVersion,
  normalizeVersion,
  emptyText = 'No version cohorts are available.',
}: VersionCohortChooserProps) {
  const [sortBy, setSortBy] = useState<CohortSort>('vulnerable')
  const selectedCohort =
    cohorts.find((cohort) => normalizeVersion(cohort.version) === selectedVersion)
    ?? cohorts[0]
    ?? null
  const sortedCohorts = useMemo(() => {
    const items = [...cohorts]

    items.sort((left, right) => {
      if (sortBy === 'recent') {
        return new Date(right.lastSeenAt).getTime() - new Date(left.lastSeenAt).getTime()
      }

      if (sortBy === 'version') {
        const leftVersion = left.version ?? ''
        const rightVersion = right.version ?? ''
        if (!leftVersion && !rightVersion) return 0
        if (!leftVersion) return 1
        if (!rightVersion) return -1
        return leftVersion.localeCompare(rightVersion, undefined, {
          numeric: true,
          sensitivity: 'base',
        })
      }

      const vulnDelta = right.activeVulnerabilityCount - left.activeVulnerabilityCount
      if (vulnDelta !== 0) return vulnDelta

      const deviceDelta = right.deviceCount - left.deviceCount
      if (deviceDelta !== 0) return deviceDelta

      return new Date(right.lastSeenAt).getTime() - new Date(left.lastSeenAt).getTime()
    })

    return items
  }, [cohorts, sortBy])

  if (cohorts.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-border/70 bg-background/40 px-4 py-6 text-sm text-muted-foreground">
        {emptyText}
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">{title}</h2>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <div className="flex flex-wrap items-start justify-end gap-3">
          <div className="rounded-2xl border border-border/70 bg-background/60 px-3 py-2.5">
            <label className="text-[10px] uppercase tracking-[0.16em] text-muted-foreground" htmlFor={`${title}-sort`}>
              Sort cohorts
            </label>
            <select
              id={`${title}-sort`}
              value={sortBy}
              onChange={(event) => setSortBy(event.target.value as CohortSort)}
              className="mt-1 block border-0 bg-transparent p-0 text-sm font-medium text-foreground focus:outline-none"
            >
              <option value="vulnerable">Most vulnerable</option>
              <option value="recent">Newest seen</option>
              <option value="version">Version</option>
            </select>
          </div>
          {selectedCohort ? (
            <div className="rounded-2xl border border-primary/20 bg-primary/8 px-4 py-3">
            <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
              Focused cohort
            </p>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <span className="text-lg font-semibold tracking-[-0.03em] text-foreground">
                {formatVersion(selectedCohort.version)}
              </span>
              <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${
                toneBadge(selectedCohort.activeVulnerabilityCount > 0 ? 'warning' : 'success')
              }`}>
                {selectedCohort.activeVulnerabilityCount} vuln{selectedCohort.activeVulnerabilityCount === 1 ? '' : 's'}
              </span>
            </div>
            <p className="mt-1 text-xs text-muted-foreground">
              {selectedCohort.activeInstallCount} installs on {selectedCohort.deviceCount} devices · detected since {formatDate(selectedCohort.lastSeenAt)}
            </p>
            </div>
          ) : null}
        </div>
      </div>

      <div className="flex gap-3 overflow-x-auto pb-1">
        {sortedCohorts.map((cohort) => {
          const versionKey = normalizeVersion(cohort.version)
          const isSelected = versionKey === selectedVersion

          return (
            <button
              key={versionKey || '__unknown__'}
              type="button"
              onClick={() => onSelectVersion(versionKey)}
              className={
                isSelected
                  ? 'min-w-[220px] shrink-0 rounded-2xl border border-primary/35 bg-primary/10 px-4 py-3 text-left shadow-[inset_0_0_0_1px_color-mix(in_oklab,var(--primary)_8%,transparent)]'
                  : 'min-w-[220px] shrink-0 rounded-2xl border border-border/70 bg-background/70 px-4 py-3 text-left hover:border-foreground/20 hover:bg-muted/20'
              }
            >
              <div className="flex items-center justify-between gap-3">
                <p className="text-sm font-semibold text-foreground">
                  {formatVersion(cohort.version)}
                </p>
                <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${
                  toneBadge(cohort.activeVulnerabilityCount > 0 ? 'warning' : 'success')
                }`}>
                  {cohort.activeVulnerabilityCount}
                </span>
              </div>
              <p className="mt-2 text-xs text-muted-foreground">
                {cohort.activeInstallCount} installs · {cohort.deviceCount} devices
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                Detected since {formatDate(cohort.lastSeenAt)}
              </p>
            </button>
          )
        })}
      </div>
    </div>
  )
}
