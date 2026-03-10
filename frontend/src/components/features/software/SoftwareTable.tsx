import { useMemo } from 'react'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import type { TenantSoftwareListItem } from '@/api/software.schemas'
import { DataTable } from '@/components/ui/data-table'
import {
  DataTableActiveFilters,
  DataTableEmptyState,
  DataTableField,
  DataTableFilterBar,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import { Input } from '@/components/ui/input'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Badge } from '@/components/ui/badge'
import { formatDate, startCase } from '@/lib/formatting'
import { SearchIcon } from 'lucide-react'

type SoftwareTableProps = {
  items: TenantSoftwareListItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  searchValue: string
  confidenceFilter: string
  vulnerableOnly: boolean
  boundOnly: boolean
  onSearchChange: (value: string) => void
  onConfidenceFilterChange: (value: string) => void
  onVulnerableOnlyChange: (value: boolean) => void
  onBoundOnlyChange: (value: boolean) => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onClearFilters: () => void
}

const confidenceOptions = ['All', 'High', 'Medium', 'Low']

export function SoftwareTable({
  items,
  totalCount,
  page,
  pageSize,
  totalPages,
  searchValue,
  confidenceFilter,
  vulnerableOnly,
  boundOnly,
  onSearchChange,
  onConfidenceFilterChange,
  onVulnerableOnlyChange,
  onBoundOnlyChange,
  onPageChange,
  onPageSizeChange,
  onClearFilters,
}: SoftwareTableProps) {
  const activeFilters = useMemo(
    () =>
      [
        searchValue ? { key: 'search', label: `Search: ${searchValue}`, onClear: () => onSearchChange('') } : null,
        confidenceFilter ? { key: 'confidence', label: `Confidence: ${confidenceFilter}`, onClear: () => onConfidenceFilterChange('') } : null,
        vulnerableOnly ? { key: 'vulnerable', label: 'Vulnerable only', onClear: () => onVulnerableOnlyChange(false) } : null,
        boundOnly ? { key: 'bound', label: 'CPE bound only', onClear: () => onBoundOnlyChange(false) } : null,
      ].filter((item): item is NonNullable<typeof item> => item !== null),
    [boundOnly, confidenceFilter, onBoundOnlyChange, onConfidenceFilterChange, onSearchChange, onVulnerableOnlyChange, searchValue, vulnerableOnly],
  )

  const columns = useMemo<ColumnDef<TenantSoftwareListItem>[]>(
    () => [
      {
        accessorKey: 'canonicalName',
        header: 'Software',
        cell: ({ row }) => (
          <div className="space-y-1">
            <Link
              to="/software/$id"
              params={{ id: row.original.id }}
              search={{ page: 1, pageSize: 25, version: '' }}
              className="font-medium underline decoration-border/70 underline-offset-4 hover:decoration-foreground"
            >
              {startCase(row.original.canonicalName)}
            </Link>
            <p className="text-xs text-muted-foreground">
              {row.original.canonicalVendor ? startCase(row.original.canonicalVendor) : 'Unknown vendor'}
            </p>
          </div>
        ),
      },
      {
        accessorKey: 'confidence',
        header: 'Identity',
        cell: ({ row }) => (
          <div className="flex flex-wrap gap-2">
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
              {row.original.confidence}
            </Badge>
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
              {row.original.normalizationMethod}
            </Badge>
          </div>
        ),
      },
      {
        accessorKey: 'primaryCpe23Uri',
        header: 'CPE',
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.primaryCpe23Uri ? 'Bound' : 'Unbound'}
          </span>
        ),
      },
      {
        accessorKey: 'activeInstallCount',
        header: 'Installs',
      },
      {
        accessorKey: 'uniqueDeviceCount',
        header: 'Devices',
      },
      {
        accessorKey: 'versionCount',
        header: 'Versions',
      },
      {
        accessorKey: 'activeVulnerabilityCount',
        header: 'Open vulns',
        cell: ({ row }) => (
          <span className={row.original.activeVulnerabilityCount > 0 ? 'font-medium text-amber-700' : 'text-muted-foreground'}>
            {row.original.activeVulnerabilityCount}
          </span>
        ),
      },
      {
          accessorKey: 'lastSeenAt',
          header: 'Last seen',
          cell: ({ row }) => (
            <span className="text-sm text-muted-foreground">
              {row.original.lastSeenAt ? formatDate(row.original.lastSeenAt) : 'Unknown'}
            </span>
          ),
        },
    ],
    [],
  )

  return (
    <DataTableWorkbench
      title="Normalized software"
      description="Browse tenant-scoped normalized products, their prevalence, and current exposure."
      totalCount={totalCount}
    >
      <DataTableToolbar>
        <DataTableToolbarRow>
          <DataTableFilterBar>
            <DataTableField label="Search">
              <div className="relative min-w-[260px]">
                <SearchIcon className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                <Input value={searchValue} onChange={(event) => onSearchChange(event.target.value)} placeholder="Search normalized software" className="pl-9" />
              </div>
            </DataTableField>
            <DataTableField label="Confidence">
              <select
                className="h-10 rounded-xl border border-input bg-background px-3 text-sm"
                value={confidenceFilter || 'All'}
                onChange={(event) => onConfidenceFilterChange(event.target.value === 'All' ? '' : event.target.value)}
              >
                {confidenceOptions.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </DataTableField>
            <label className="flex items-center gap-2 text-sm text-muted-foreground">
              <input type="checkbox" checked={vulnerableOnly} onChange={(event) => onVulnerableOnlyChange(event.target.checked)} />
              Vulnerable only
            </label>
            <label className="flex items-center gap-2 text-sm text-muted-foreground">
              <input type="checkbox" checked={boundOnly} onChange={(event) => onBoundOnlyChange(event.target.checked)} />
              CPE bound only
            </label>
          </DataTableFilterBar>
        </DataTableToolbarRow>
        <DataTableToolbarRow>
          <DataTableActiveFilters filters={activeFilters} onClearAll={onClearFilters} />
        </DataTableToolbarRow>
      </DataTableToolbar>

      {items.length === 0 ? (
        <DataTableEmptyState
          title="No software found"
          description="Try broadening the search or clearing one of the active filters."
        />
      ) : (
        <DataTable columns={columns} data={items} />
      )}

      <div className="flex items-center justify-between gap-4">
        <p className="text-sm text-muted-foreground">{totalCount} normalized software record{totalCount === 1 ? '' : 's'}</p>
        <PaginationControls
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          totalPages={totalPages}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
        />
      </div>
    </DataTableWorkbench>
  )
}
