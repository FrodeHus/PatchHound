import { useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { ArrowDown, ArrowUp, SearchIcon } from 'lucide-react'
import type { DashboardRiskChangeBrief } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import {
  DataTableActiveFilters,
  DataTableEmptyState,
  DataTableField,
  DataTableFilterBar,
  DataTableSummaryStrip,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { formatDateTime } from '@/lib/formatting'
import { toneBadge } from '@/lib/tone-classes'
import { vulnerabilitySeverityOptions } from '@/lib/options/vulnerabilities'

type RiskChangeWorkbenchProps = {
  brief: DashboardRiskChangeBrief
}

type ChangeType = 'all' | 'appeared' | 'resolved'
type SortMode = 'recent' | 'assets' | 'severity' | 'externalId'

type RiskChangeRow = DashboardRiskChangeBrief['appeared'][number] & {
  changeType: Exclude<ChangeType, 'all'>
}

const severityRank: Record<string, number> = {
  Critical: 4,
  High: 3,
  Medium: 2,
  Low: 1,
}

export function RiskChangeWorkbench({ brief }: RiskChangeWorkbenchProps) {
  const [search, setSearch] = useState('')
  const [changeType, setChangeType] = useState<ChangeType>('all')
  const [sortMode, setSortMode] = useState<SortMode>('recent')
  const [selectedSeverities, setSelectedSeverities] = useState<string[]>(['High', 'Critical'])

  const allRows = useMemo<RiskChangeRow[]>(
    () => [
      ...brief.appeared.map((item) => ({ ...item, changeType: 'appeared' as const })),
      ...brief.resolved.map((item) => ({ ...item, changeType: 'resolved' as const })),
    ],
    [brief.appeared, brief.resolved],
  )

  const filteredRows = useMemo(() => {
    const normalizedSearch = search.trim().toLowerCase()

    return allRows
      .filter((row) => {
        if (changeType !== 'all' && row.changeType !== changeType) {
          return false
        }

        if (selectedSeverities.length > 0 && !selectedSeverities.includes(row.severity)) {
          return false
        }

        if (!normalizedSearch) {
          return true
        }

        return (
          row.externalId.toLowerCase().includes(normalizedSearch)
          || row.title.toLowerCase().includes(normalizedSearch)
        )
      })
      .sort((left, right) => {
        switch (sortMode) {
          case 'assets':
            return right.affectedAssetCount - left.affectedAssetCount || compareDates(right.changedAt, left.changedAt)
          case 'severity':
            return (severityRank[right.severity] ?? 0) - (severityRank[left.severity] ?? 0)
              || compareDates(right.changedAt, left.changedAt)
          case 'externalId':
            return left.externalId.localeCompare(right.externalId)
          case 'recent':
          default:
            return compareDates(right.changedAt, left.changedAt)
        }
      })
  }, [allRows, changeType, search, selectedSeverities, sortMode])

  const activeFilters = useMemo(() => {
    const filters: { key: string; label: string; onClear: () => void }[] = []

    if (changeType !== 'all') {
      filters.push({
        key: 'changeType',
        label: changeType === 'appeared' ? 'Change: New' : 'Change: Resolved',
        onClear: () => setChangeType('all'),
      })
    }

    if (search.trim()) {
      filters.push({
        key: 'search',
        label: `Search: ${search.trim()}`,
        onClear: () => setSearch(''),
      })
    }

    if (selectedSeverities.length !== vulnerabilitySeverityOptions.length) {
      filters.push({
        key: 'severity',
        label: `Severity: ${selectedSeverities.join(', ')}`,
        onClear: () => setSelectedSeverities(['High', 'Critical']),
      })
    }

    return filters
  }, [changeType, search, selectedSeverities])

  const summaryItems = useMemo(
    () => [
      { label: 'Appeared', value: brief.appearedCount.toString(), tone: 'warning' as const },
      { label: 'Resolved', value: brief.resolvedCount.toString(), tone: 'accent' as const },
      {
        label: 'Critical changes',
        value: allRows.filter((row) => row.severity === 'Critical').length.toString(),
      },
      {
        label: 'Net change',
        value: `${brief.appearedCount - brief.resolvedCount >= 0 ? '+' : ''}${brief.appearedCount - brief.resolvedCount}`,
      },
    ],
    [allRows, brief.appearedCount, brief.resolvedCount],
  )

  const columns = useMemo<ColumnDef<RiskChangeRow>[]>(
    () => [
      {
        accessorKey: 'changeType',
        header: ({ column }) => <SortableColumnHeader column={column} title="Change" />,
        cell: ({ row }) => (
          <Badge
            variant="outline"
            className={`rounded-full ${row.original.changeType === 'appeared' ? toneBadge('warning') : toneBadge('success')}`}
          >
            {row.original.changeType === 'appeared' ? (
              <>
                <ArrowUp className="mr-1 size-3" />
                New
              </>
            ) : (
              <>
                <ArrowDown className="mr-1 size-3" />
                Resolved
              </>
            )}
          </Badge>
        ),
      },
      {
        accessorKey: 'externalId',
        header: ({ column }) => <SortableColumnHeader column={column} title="CVE" />,
        cell: ({ row }) => (
          <Link
            to="/vulnerabilities/$id"
            params={{ id: row.original.vulnerabilityId }}
            className="font-medium underline decoration-border/70 underline-offset-4 transition hover:text-primary hover:decoration-foreground"
          >
            {row.original.externalId}
          </Link>
        ),
      },
      {
        accessorKey: 'title',
        header: ({ column }) => <SortableColumnHeader column={column} title="Title" />,
        cell: ({ row }) => (
          <div className="max-w-[30rem]">
            <p className="truncate text-sm text-foreground">{row.original.title}</p>
          </div>
        ),
      },
      {
        accessorKey: 'severity',
        header: ({ column }) => <SortableColumnHeader column={column} title="Severity" />,
        cell: ({ row }) => (
          <Badge
            variant="outline"
            className={
              row.original.severity === 'Critical'
                ? `rounded-full ${toneBadge('danger')}`
                : row.original.severity === 'High'
                  ? `rounded-full ${toneBadge('warning')}`
                  : `rounded-full ${toneBadge('neutral')}`
            }
          >
            {row.original.severity}
          </Badge>
        ),
      },
      {
        accessorKey: 'affectedAssetCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Affected assets" />,
        cell: ({ row }) => <span className="text-sm font-medium">{row.original.affectedAssetCount}</span>,
      },
      {
        accessorKey: 'changedAt',
        header: ({ column }) => <SortableColumnHeader column={column} title="Changed" />,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">{formatDateTime(row.original.changedAt)}</span>
        ),
      },
    ],
    [],
  )

  return (
    <DataTableWorkbench
      title="Risk change log"
      description="Review what entered or exited the tenant in the last 24 hours. High and critical issues are selected by default, but you can widen the lens when needed."
      totalCount={filteredRows.length}
    >
      <DataTableToolbar>
        {brief.aiSummary ? (
          <InsetPanel className="px-4 py-3">
            <p className="text-sm text-muted-foreground">{brief.aiSummary}</p>
          </InsetPanel>
        ) : null}

        <DataTableSummaryStrip items={summaryItems} />

        <DataTableToolbarRow>
          <div className="flex flex-wrap items-center gap-2">
            <ChangeTypeButton
              active={changeType === 'all'}
              onClick={() => setChangeType('all')}
              count={brief.appearedCount + brief.resolvedCount}
            >
              All
            </ChangeTypeButton>
            <ChangeTypeButton
              active={changeType === 'appeared'}
              onClick={() => setChangeType('appeared')}
              count={brief.appearedCount}
            >
              New
            </ChangeTypeButton>
            <ChangeTypeButton
              active={changeType === 'resolved'}
              onClick={() => setChangeType('resolved')}
              count={brief.resolvedCount}
            >
              Resolved
            </ChangeTypeButton>
          </div>
          <Badge variant="outline" className="rounded-full border-border/70 bg-muted text-foreground">
            Last 24 hours
          </Badge>
        </DataTableToolbarRow>

        <DataTableFilterBar className="lg:grid-cols-[minmax(0,1.5fr)_minmax(180px,0.75fr)_minmax(220px,1fr)]">
          <DataTableField label="Search">
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search CVE or title"
                className="pl-9"
              />
            </div>
          </DataTableField>

          <DataTableField label="Sort by">
            <Select value={sortMode} onValueChange={(value) => setSortMode(value as SortMode)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="recent">Most recent</SelectItem>
                <SelectItem value="assets">Most affected assets</SelectItem>
                <SelectItem value="severity">Severity</SelectItem>
                <SelectItem value="externalId">External ID</SelectItem>
              </SelectContent>
            </Select>
          </DataTableField>

          <DataTableField label="Severity">
            <div className="flex flex-wrap gap-2">
              {vulnerabilitySeverityOptions.map((severity) => {
                const active = selectedSeverities.includes(severity)
                return (
                  <Button
                    key={severity}
                    type="button"
                    size="sm"
                    variant={active ? 'default' : 'outline'}
                    className="rounded-full"
                    onClick={() => {
                      setSelectedSeverities((current) => {
                        return current.includes(severity)
                          ? current.filter((value) => value !== severity)
                          : [...current, severity]
                      })
                    }}
                  >
                    {severity}
                  </Button>
                )
              })}
            </div>
          </DataTableField>
        </DataTableFilterBar>

        <DataTableActiveFilters
          filters={activeFilters}
          onClearAll={() => {
            setSearch('')
            setChangeType('all')
            setSelectedSeverities(['High', 'Critical'])
          }}
        />
      </DataTableToolbar>

      <DataTable
        columns={columns}
        data={filteredRows}
        getRowId={(row) => `${row.changeType}:${row.vulnerabilityId}`}
        emptyState={
          <DataTableEmptyState
            title="No matching changes"
            description="Try widening the selected severities or clearing the current search and change filters."
          />
        }
      />
    </DataTableWorkbench>
  )
}

function ChangeTypeButton({
  active,
  onClick,
  count,
  children,
}: {
  active: boolean
  onClick: () => void
  count: number
  children: string
}) {
  return (
    <Button type="button" variant={active ? 'default' : 'outline'} className="rounded-full" onClick={onClick}>
      {children}
      <span className="ml-1 text-xs opacity-80">{count}</span>
    </Button>
  )
}

function compareDates(left: string, right: string) {
  return new Date(left).getTime() - new Date(right).getTime()
}
