import { useCallback, useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import type { DecisionVuln } from '@/api/remediation.schemas'
import { addVulnerabilityOverride } from '@/api/remediation.functions'
import { DataTable } from '@/components/ui/data-table'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { Button } from '@/components/ui/button'
import { toneBadge } from '@/lib/tone-classes'
import { severityTone, outcomeLabel, outcomeTone } from './remediation-utils'

type RemediationVulnTableProps = {
  vulnerabilities: DecisionVuln[]
  decisionId: string | null
  assetId: string
  queryKey: readonly unknown[]
  onSelectVuln: (vuln: DecisionVuln) => void
}

export function RemediationVulnTable({
  vulnerabilities,
  decisionId,
  assetId,
  queryKey,
  onSelectVuln,
}: RemediationVulnTableProps) {
  const queryClient = useQueryClient()
  const [overridingId, setOverridingId] = useState<string | null>(null)

  const handleOverride = useCallback(async (vuln: DecisionVuln, outcome: string) => {
    if (!decisionId) return
    setOverridingId(vuln.tenantVulnerabilityId)
    try {
      await addVulnerabilityOverride({
        data: {
          assetId,
          decisionId,
          tenantVulnerabilityId: vuln.tenantVulnerabilityId,
          outcome,
          justification: `Per-vulnerability override to ${outcome}`,
        },
      })
      await queryClient.invalidateQueries({ queryKey })
    } finally {
      setOverridingId(null)
    }
  }, [decisionId, assetId, queryClient, queryKey])

  const columns = useMemo<ColumnDef<DecisionVuln>[]>(
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
              {row.original.effectiveSeverity ?? row.original.vendorSeverity}
              {score != null ? ` ${score.toFixed(1)}` : ''}
            </span>
          )
        },
      },
      {
        id: 'epss',
        accessorFn: (row) => row.epssScore ?? -1,
        header: ({ column }) => <SortableColumnHeader column={column} title="EPSS" />,
        cell: ({ row }) => {
          const epss = row.original.epssScore
          if (epss == null) return <span className="text-muted-foreground">-</span>
          return <span className="tabular-nums">{(epss * 100).toFixed(1)}%</span>
        },
      },
      {
        id: 'riskScore',
        accessorFn: (row) => row.episodeRiskScore ?? -1,
        header: ({ column }) => <SortableColumnHeader column={column} title="Risk" />,
        cell: ({ row }) => {
          const score = row.original.episodeRiskScore
          if (score == null) return <span className="text-muted-foreground">-</span>
          return <span className="tabular-nums font-medium">{score.toFixed(0)}</span>
        },
      },
      {
        id: 'threats',
        header: 'Threats',
        enableSorting: false,
        cell: ({ row }) => {
          const v = row.original
          const badges: string[] = []
          if (v.knownExploited) badges.push('KEV')
          if (v.publicExploit) badges.push('Exploit')
          if (v.activeAlert) badges.push('Alert')
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
        id: 'override',
        header: 'Override',
        enableSorting: false,
        cell: ({ row }) => {
          const v = row.original
          if (v.overrideOutcome) {
            return (
              <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(v.overrideOutcome))}`}>
                {outcomeLabel(v.overrideOutcome)}
              </span>
            )
          }
          if (!decisionId) return <span className="text-muted-foreground">-</span>
          const isLoading = overridingId === v.tenantVulnerabilityId
          return (
            <Button
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              disabled={isLoading}
              onClick={() => handleOverride(v, 'RiskAcceptance')}
            >
              {isLoading ? '...' : 'Override'}
            </Button>
          )
        },
      },
    ],
    [onSelectVuln, decisionId, overridingId, handleOverride],
  )

  return (
    <DataTable
      columns={columns}
      data={vulnerabilities}
      getRowId={(row) => row.tenantVulnerabilityId}
      emptyState={
        <div className="py-12 text-center text-muted-foreground">
          No vulnerabilities found for this software asset.
        </div>
      }
    />
  )
}
