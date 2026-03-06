import { createFileRoute } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import {
  type VulnerabilityListFilters,
  useVulnerabilities,
} from '@/api/useVulnerabilities'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'

export const Route = createFileRoute('/vulnerabilities/')({
  component: VulnerabilitiesPage,
})

function VulnerabilitiesPage() {
  const [search, setSearch] = useState('')
  const [severity, setSeverity] = useState('')
  const [status, setStatus] = useState('')

  const filters = useMemo<VulnerabilityListFilters>(() => ({
    search: search.trim() || undefined,
    severity: severity || undefined,
    status: status || undefined,
    page: 1,
    pageSize: 25,
  }), [search, severity, status])

  const vulnerabilitiesQuery = useVulnerabilities(filters)

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Vulnerabilities</h1>

      <div className="grid gap-2 rounded-lg border border-border bg-card p-4 md:grid-cols-3">
        <input
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          placeholder="Search CVE or title"
          value={search}
          onChange={(event) => {
            setSearch(event.target.value)
          }}
        />
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={severity}
          onChange={(event) => {
            setSeverity(event.target.value)
          }}
        >
          <option value="">All severities</option>
          <option value="Critical">Critical</option>
          <option value="High">High</option>
          <option value="Medium">Medium</option>
          <option value="Low">Low</option>
        </select>
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={status}
          onChange={(event) => {
            setStatus(event.target.value)
          }}
        >
          <option value="">All statuses</option>
          <option value="Open">Open</option>
          <option value="InRemediation">In Remediation</option>
          <option value="Resolved">Resolved</option>
          <option value="RiskAccepted">Risk Accepted</option>
        </select>
      </div>

      {vulnerabilitiesQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading vulnerabilities...</p> : null}
      {vulnerabilitiesQuery.isError ? <p className="text-sm text-destructive">Failed to load vulnerabilities.</p> : null}

      {vulnerabilitiesQuery.data ? (
        <VulnerabilityTable
          items={vulnerabilitiesQuery.data.items}
          totalCount={vulnerabilitiesQuery.data.totalCount}
        />
      ) : null}
    </section>
  )
}
