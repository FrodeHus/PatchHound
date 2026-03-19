import type { ColumnDef } from '@tanstack/react-table'
import type { VulnerabilityDetail } from '@/api/vulnerabilities.schemas'
import { DataTable } from '@/components/ui/data-table'
import { toneBadge } from '@/lib/tone-classes'
import { ExternalLinkIcon } from 'lucide-react'

type Reference = VulnerabilityDetail['references'][number]

type ReferencesTabProps = {
  references: Reference[]
}

const columns: ColumnDef<Reference>[] = [
  {
    accessorKey: 'source',
    header: 'Source',
    cell: ({ row }) => (
      <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${toneBadge('neutral')}`}>
        {row.original.source}
      </span>
    ),
  },
  {
    accessorKey: 'url',
    header: 'URL',
    cell: ({ row }) => (
      <a
        href={row.original.url}
        target="_blank"
        rel="noreferrer"
        className="inline-flex items-center gap-1.5 font-medium text-primary hover:underline"
      >
        <span className="break-all text-sm">{row.original.url}</span>
        <ExternalLinkIcon className="size-3.5 shrink-0" />
      </a>
    ),
  },
  {
    accessorKey: 'tags',
    header: 'Tags',
    cell: ({ row }) =>
      row.original.tags.length > 0 ? (
        <div className="flex flex-wrap gap-1.5">
          {row.original.tags.map((tag) => (
            <span
              key={tag}
              className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge('info')}`}
            >
              {tag}
            </span>
          ))}
        </div>
      ) : (
        <span className="text-sm text-muted-foreground">—</span>
      ),
  },
]

export function ReferencesTab({ references }: ReferencesTabProps) {
  if (references.length === 0) {
    return (
      <section className="rounded-2xl border border-border/70 bg-card p-5">
        <h3 className="text-lg font-semibold">References</h3>
        <p className="mt-1 text-sm text-muted-foreground">No external references are linked to this vulnerability.</p>
      </section>
    )
  }

  return (
    <section className="rounded-2xl border border-border/70 bg-card p-5">
      <div className="mb-4 space-y-1">
        <h3 className="text-lg font-semibold">References</h3>
        <p className="text-sm text-muted-foreground">
          {references.length} external {references.length === 1 ? 'advisory' : 'advisories'} and research links
        </p>
      </div>

      <div className="overflow-hidden rounded-xl border border-border/70">
        <DataTable
          columns={columns}
          data={references}
          getRowId={(row, idx) => `${row.source}:${idx}`}
        />
      </div>
    </section>
  )
}
