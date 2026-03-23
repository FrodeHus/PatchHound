import { useMemo } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { SoftwareRemediationVuln } from '@/api/remediation.schemas'
import { DataTable } from '@/components/ui/data-table'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { toneBadge, type Tone } from '@/lib/tone-classes'

type RemediationVulnTableProps = {
  vulnerabilities: SoftwareRemediationVuln[]
  onSelectVuln: (vuln: SoftwareRemediationVuln) => void
}

function severityTone(severity: string): Tone {
  switch (severity) {
    case 'Critical': return 'danger'
    case 'High': return 'warning'
    case 'Medium': return 'info'
    default: return 'neutral'
  }
}

function taskStatusLabel(vuln: SoftwareRemediationVuln): string {
  if (vuln.riskAcceptance?.status === 'Approved') return 'Risk Accepted'
  if (vuln.remediationTask) return vuln.remediationTask.status
  return 'No task'
}

function taskStatusTone(vuln: SoftwareRemediationVuln): Tone {
  if (vuln.riskAcceptance?.status === 'Approved') return 'success'
  if (!vuln.remediationTask) return 'neutral'
  switch (vuln.remediationTask.status) {
    case 'Completed': return 'success'
    case 'InProgress':
    case 'PatchScheduled': return 'info'
    case 'Pending': return 'warning'
    case 'CannotPatch': return 'danger'
    default: return 'neutral'
  }
}

export function RemediationVulnTable({ vulnerabilities, onSelectVuln }: RemediationVulnTableProps) {
  const columns = useMemo<ColumnDef<SoftwareRemediationVuln>[]>(
    () => [
      {
        accessorKey: 'externalId',
        header: ({ column }) => <SortableColumnHeader column={column} title="CVE" />,
        cell: ({ row }) => (
          <button
            type="button"
            onClick={() => onSelectVuln(row.original)}
            className="text-left font-medium text-primary underline decoration-primary/30 underline-offset-2 hover:decoration-primary/60"
          >
            {row.original.externalId}
          </button>
        ),
      },
      {
        accessorKey: 'title',
        header: 'Title',
        enableSorting: false,
        cell: ({ row }) => (
          <span className="line-clamp-1 max-w-[280px]" title={row.original.title}>
            {row.original.title}
          </span>
        ),
      },
      {
        accessorKey: 'effectiveScore',
        header: ({ column }) => <SortableColumnHeader column={column} title="Score" />,
        cell: ({ row }) => {
          const score = row.original.effectiveScore
          const tone = severityTone(row.original.effectiveSeverity)
          return (
            <span className={`inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(tone)}`}>
              {row.original.effectiveSeverity}
              {score != null ? ` ${score.toFixed(1)}` : ''}
            </span>
          )
        },
      },
      {
        id: 'epss',
        accessorFn: (row) => row.threat?.epssScore ?? -1,
        header: ({ column }) => <SortableColumnHeader column={column} title="EPSS" />,
        cell: ({ row }) => {
          const epss = row.original.threat?.epssScore
          if (epss == null) return <span className="text-muted-foreground">-</span>
          return <span className="tabular-nums">{(epss * 100).toFixed(1)}%</span>
        },
      },
      {
        id: 'threats',
        header: 'Threats',
        enableSorting: false,
        cell: ({ row }) => {
          const threat = row.original.threat
          if (!threat) return <span className="text-muted-foreground">-</span>
          const badges: string[] = []
          if (threat.knownExploited) badges.push('KEV')
          if (threat.publicExploit) badges.push('Exploit')
          if (threat.activeAlert) badges.push('Alert')
          if (threat.hasRansomwareAssociation) badges.push('Ransomware')
          if (badges.length === 0) return <span className="text-muted-foreground">-</span>
          return (
            <div className="flex flex-wrap gap-1">
              {badges.map((b) => (
                <span key={b} className={`rounded-full border px-1.5 py-0.5 text-[10px] font-medium ${toneBadge('danger')}`}>
                  {b}
                </span>
              ))}
            </div>
          )
        },
      },
      {
        accessorKey: 'confidence',
        header: 'Match',
        enableSorting: false,
        cell: ({ row }) => (
          <span className="text-xs text-muted-foreground">{row.original.confidence}</span>
        ),
      },
      {
        id: 'status',
        header: 'Status',
        enableSorting: false,
        cell: ({ row }) => {
          const label = taskStatusLabel(row.original)
          const tone = taskStatusTone(row.original)
          return (
            <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(tone)}`}>
              {label}
            </span>
          )
        },
      },
    ],
    [onSelectVuln],
  )

  return (
    <DataTable
      columns={columns}
      data={vulnerabilities}
      getRowId={(row) => row.vulnerabilityDefinitionId}
      emptyState={
        <div className="py-12 text-center text-muted-foreground">
          No vulnerabilities found for this software asset.
        </div>
      }
    />
  )
}
