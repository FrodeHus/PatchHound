import { useEffect, useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import type { AffectedAsset } from '@/api/vulnerabilities.schemas'
import { DataTable } from '@/components/ui/data-table'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { toneBadge, toneText } from '@/lib/tone-classes'
import { formatDateTime } from '@/lib/formatting'
import { SearchIcon, ChevronLeftIcon, ChevronRightIcon } from 'lucide-react'

type AffectedAssetsTabProps = {
  assets: AffectedAsset[]
}

const PAGE_SIZE = 25

const columns: ColumnDef<AffectedAsset>[] = [
  {
    accessorKey: 'assetName',
    header: ({ column }) => <SortableColumnHeader column={column} title="Asset" />,
    cell: ({ row }) => (
      <div className="space-y-0.5">
        <Link
          to="/assets/$id"
          params={{ id: row.original.assetId }}
          className="font-medium underline decoration-border/70 underline-offset-4 hover:decoration-foreground"
        >
          {row.original.assetName}
        </Link>
        <p className="text-xs text-muted-foreground">{row.original.assetType}</p>
      </div>
    ),
  },
  {
    accessorKey: 'status',
    header: ({ column }) => <SortableColumnHeader column={column} title="Status" />,
    cell: ({ row }) => (
      <span
        className={`inline-flex rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${
          row.original.status === 'Open' ? toneBadge('warning') : toneBadge('success')
        }`}
      >
        {row.original.status}
      </span>
    ),
  },
  {
    accessorKey: 'episodeRiskScore',
    header: ({ column }) => <SortableColumnHeader column={column} title="Risk" />,
    cell: ({ row }) => {
      const score = row.original.episodeRiskScore
      const band = row.original.episodeRiskBand
      if (score == null) {
        return <span className="text-sm text-muted-foreground">—</span>
      }

      return (
        <div className="space-y-0.5">
          <p className="text-sm font-medium">
            {score.toFixed(0)}
            {band ? ` · ${band}` : ''}
          </p>
          <p className="text-xs text-muted-foreground">Episode risk</p>
        </div>
      )
    },
  },
  {
    accessorKey: 'effectiveSeverity',
    header: ({ column }) => <SortableColumnHeader column={column} title="Eff. severity" />,
    cell: ({ row }) => {
      const { effectiveSeverity, effectiveScore } = row.original
      const tone = severityTone(effectiveSeverity)
      return (
        <span className={`text-sm font-medium ${toneText(tone)}`}>
          {effectiveSeverity}
          {effectiveScore != null ? ` (${effectiveScore.toFixed(1)})` : ''}
        </span>
      )
    },
  },
  {
    accessorKey: 'securityProfileName',
    header: ({ column }) => <SortableColumnHeader column={column} title="Profile" />,
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.securityProfileName ?? 'None'}
      </span>
    ),
  },
  {
    accessorKey: 'episodeCount',
    header: ({ column }) => <SortableColumnHeader column={column} title="Episodes" />,
    cell: ({ row }) => {
      const count = row.original.episodeCount
      return (
        <span className={count > 1 ? `font-medium ${toneText('warning')}` : 'text-muted-foreground'}>
          {count}
          {count > 1 ? ' (recurred)' : ''}
        </span>
      )
    },
  },
  {
    accessorKey: 'detectedDate',
    header: ({ column }) => <SortableColumnHeader column={column} title="Detected" />,
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">{formatDateTime(row.original.detectedDate)}</span>
    ),
  },
  {
    accessorKey: 'resolvedDate',
    header: ({ column }) => <SortableColumnHeader column={column} title="Resolved" />,
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.resolvedDate ? formatDateTime(row.original.resolvedDate) : '—'}
      </span>
    ),
  },
]

export function AffectedAssetsTab({ assets }: AffectedAssetsTabProps) {
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<string>('all')
  const [page, setPage] = useState(1)

  useEffect(() => {
    const id = window.setTimeout(() => {
      setDebouncedSearch(search)
      setPage(1)
    }, 250)
    return () => window.clearTimeout(id)
  }, [search])

  const filtered = useMemo(() => {
    let result = assets
  if (debouncedSearch) {
      const q = debouncedSearch.toLowerCase()
      result = result.filter((a) =>
        a.assetName.toLowerCase().includes(q)
        || (a.episodeRiskBand ?? '').toLowerCase().includes(q)
      )
    }
    if (statusFilter !== 'all') {
      result = result.filter((a) => a.status === statusFilter)
    }
    return result
  }, [assets, debouncedSearch, statusFilter])

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const paged = useMemo(
    () => filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE),
    [filtered, page],
  )

  const statusCounts = useMemo(() => {
    const open = assets.filter((a) => a.status === 'Open').length
    return { open, resolved: assets.length - open }
  }, [assets])

  if (assets.length === 0) {
    return (
      <section className="rounded-2xl border border-border/70 bg-card p-5">
        <h3 className="text-lg font-semibold">Affected Assets</h3>
        <p className="mt-1 text-sm text-muted-foreground">No affected assets are currently linked to this vulnerability.</p>
      </section>
    )
  }

  return (
    <section className="rounded-2xl border border-border/70 bg-card p-5">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h3 className="text-lg font-semibold">Affected Assets</h3>
          <p className="text-sm text-muted-foreground">
            {statusCounts.open} open, {statusCounts.resolved} resolved — {assets.length} total
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <div className="relative min-w-[200px]">
            <SearchIcon className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Filter by name…"
              className="h-9 pl-9 text-sm"
            />
          </div>
          <Select value={statusFilter} onValueChange={(v) => { setStatusFilter(v ?? 'all'); setPage(1) }}>
            <SelectTrigger className="h-9 w-[130px] rounded-xl border-border/70 bg-background/80 text-sm">
              <SelectValue />
            </SelectTrigger>
            <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="Open">Open</SelectItem>
              <SelectItem value="Resolved">Resolved</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      <div className="overflow-hidden rounded-xl border border-border/70">
        <DataTable
          columns={columns}
          data={paged}
          getRowId={(row) => row.assetId}
          emptyState={<span className="text-sm text-muted-foreground">No matching assets.</span>}
        />
      </div>

      <div className="mt-4 flex items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">
          {filtered.length === assets.length
            ? `${assets.length} assets`
            : `${filtered.length} of ${assets.length} assets`}
        </p>
        {totalPages > 1 && (
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={page <= 1}
              onClick={() => setPage((p) => p - 1)}
              className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-border/70 bg-background text-sm text-muted-foreground hover:bg-muted/50 disabled:pointer-events-none disabled:opacity-40"
            >
              <ChevronLeftIcon className="size-4" />
            </button>
            <span className="text-sm text-muted-foreground">
              {page} / {totalPages}
            </span>
            <button
              type="button"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
              className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-border/70 bg-background text-sm text-muted-foreground hover:bg-muted/50 disabled:pointer-events-none disabled:opacity-40"
            >
              <ChevronRightIcon className="size-4" />
            </button>
          </div>
        )}
      </div>
    </section>
  )
}

function severityTone(severity: string) {
  switch (severity.toLowerCase()) {
    case 'critical':
      return 'danger' as const
    case 'high':
      return 'warning' as const
    case 'medium':
      return 'info' as const
    default:
      return 'neutral' as const
  }
}
