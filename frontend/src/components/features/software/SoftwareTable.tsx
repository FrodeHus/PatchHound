import { useEffect, useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import type { TenantSoftwareListItem } from '@/api/software.schemas'
import { DataTable } from '@/components/ui/data-table'
import {
  DataTableActiveFilters,
  DataTableEmptyState,
  DataTableField,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import { Input } from '@/components/ui/input'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Badge } from '@/components/ui/badge'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { WorkbenchFilterDrawer, WorkbenchFilterSection } from '@/components/ui/workbench-filter-drawer'
import { formatDate, startCase } from '@/lib/formatting'
import { toneText } from '@/lib/tone-classes'
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
  onApplyStructuredFilters: (filters: {
    confidence: string
    vulnerableOnly: boolean
    boundOnly: boolean
  }) => void
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
  onApplyStructuredFilters,
  onPageChange,
  onPageSizeChange,
  onClearFilters,
}: SoftwareTableProps) {
  const [searchInput, setSearchInput] = useState(searchValue)
  const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false)
  const [draftFilters, setDraftFilters] = useState({
    confidence: confidenceFilter,
    vulnerableOnly,
    boundOnly,
  })

  useEffect(() => {
    setSearchInput(searchValue)
  }, [searchValue])

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      if (searchInput !== searchValue) {
        onSearchChange(searchInput)
      }
    }, 350)

    return () => {
      window.clearTimeout(timeoutId)
    }
  }, [onSearchChange, searchInput, searchValue])

  useEffect(() => {
    if (!isFilterDrawerOpen) {
      setDraftFilters({
        confidence: confidenceFilter,
        vulnerableOnly,
        boundOnly,
      })
    }
  }, [boundOnly, confidenceFilter, isFilterDrawerOpen, vulnerableOnly])

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

  const activeStructuredFilterCount = useMemo(
    () =>
      [
        confidenceFilter,
        vulnerableOnly ? 'vulnerable' : '',
        boundOnly ? 'bound' : '',
      ].filter(Boolean).length,
    [boundOnly, confidenceFilter, vulnerableOnly],
  )

  const columns = useMemo<ColumnDef<TenantSoftwareListItem>[]>(
    () => [
      {
        accessorKey: 'canonicalName',
        header: ({ column }) => <SortableColumnHeader column={column} title="Software" />,
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
        header: ({ column }) => <SortableColumnHeader column={column} title="Identity" />,
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
        header: ({ column }) => <SortableColumnHeader column={column} title="CPE" />,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.primaryCpe23Uri ? 'Bound' : 'Unbound'}
          </span>
        ),
      },
      {
        accessorKey: 'activeInstallCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Installs" />,
      },
      {
        accessorKey: 'uniqueDeviceCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Devices" />,
      },
      {
        accessorKey: 'versionCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Versions" />,
      },
      {
        accessorKey: 'activeVulnerabilityCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Open vulns" />,
        cell: ({ row }) => (
          <span className={row.original.activeVulnerabilityCount > 0 ? `font-medium ${toneText('warning')}` : 'text-muted-foreground'}>
            {row.original.activeVulnerabilityCount}
          </span>
        ),
      },
      {
        accessorKey: 'exposureImpactScore',
        header: ({ column }) => <SortableColumnHeader column={column} title="Impact" />,
        cell: ({ row }) => {
          const score = row.original.exposureImpactScore
          if (score == null) return <span className="text-muted-foreground">—</span>
          const tone = score >= 75 ? 'danger' : score >= 40 ? 'warning' : score >= 10 ? 'info' : 'success'
          return <span className={`font-medium ${toneText(tone)}`}>{score.toFixed(1)}</span>
        },
      },
      {
          accessorKey: 'lastSeenAt',
          header: ({ column }) => <SortableColumnHeader column={column} title="Last seen" />,
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
          <DataTableField label="Search" className="flex-1">
            <div className="relative min-w-[260px]">
              <SearchIcon className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={searchInput}
                onChange={(event) => setSearchInput(event.target.value)}
                placeholder="Search normalized software"
                className="pl-9"
              />
            </div>
          </DataTableField>
          <Button
            type="button"
            variant="outline"
            className="h-10 rounded-xl border-border/70 bg-background/80 px-4"
            onClick={() => {
              setDraftFilters({
                confidence: confidenceFilter,
                vulnerableOnly,
                boundOnly,
              })
              setIsFilterDrawerOpen(true)
            }}
          >
            {activeStructuredFilterCount > 0 ? `Filters (${activeStructuredFilterCount})` : 'Filters...'}
          </Button>
        </DataTableToolbarRow>
        <DataTableToolbarRow>
          <DataTableActiveFilters filters={activeFilters} onClearAll={onClearFilters} />
        </DataTableToolbarRow>
      </DataTableToolbar>

      <WorkbenchFilterDrawer
        open={isFilterDrawerOpen}
        onOpenChange={setIsFilterDrawerOpen}
        title="Software Filters"
        description="Refine the normalized catalog by identity confidence and exposure state."
        activeCount={activeStructuredFilterCount}
        onResetDraft={() => {
          setDraftFilters({
            confidence: '',
            vulnerableOnly: false,
            boundOnly: false,
          })
        }}
        onApply={() => {
          onApplyStructuredFilters(draftFilters)
          setIsFilterDrawerOpen(false)
        }}
      >
        <WorkbenchFilterSection
          title="Identity"
          description="Focus on the confidence level of the normalized software identity."
        >
          <DataTableField label="Confidence">
            <Select
              value={draftFilters.confidence || 'all'}
              onValueChange={(value) => {
                const nextValue = value ?? 'all'
                setDraftFilters((current) => ({
                  ...current,
                  confidence: nextValue === 'all' ? '' : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any confidence" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any confidence</SelectItem>
                {confidenceOptions.filter((option) => option !== 'All').map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>
        </WorkbenchFilterSection>

        <WorkbenchFilterSection
          title="Exposure"
          description="Limit the catalog to software with current exposure or existing CPE coverage."
        >
          <label className="flex items-center gap-3 rounded-xl border border-border/70 bg-background/50 px-3 py-3 text-sm">
            <Checkbox
              checked={draftFilters.vulnerableOnly}
              onCheckedChange={(checked) => {
                setDraftFilters((current) => ({
                  ...current,
                  vulnerableOnly: checked === true,
                }))
              }}
            />
            <span>Vulnerable only</span>
          </label>

          <label className="flex items-center gap-3 rounded-xl border border-border/70 bg-background/50 px-3 py-3 text-sm">
            <Checkbox
              checked={draftFilters.boundOnly}
              onCheckedChange={(checked) => {
                setDraftFilters((current) => ({
                  ...current,
                  boundOnly: checked === true,
                }))
              }}
            />
            <span>CPE bound only</span>
          </label>
        </WorkbenchFilterSection>
      </WorkbenchFilterDrawer>

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
