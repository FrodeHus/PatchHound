import { Button } from '@/components/ui/button'

const pageSizeOptions = [25, 50, 100]

type PaginationControlsProps = {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
}

export function PaginationControls({
  page,
  pageSize,
  totalCount,
  totalPages,
  onPageChange,
  onPageSizeChange,
}: PaginationControlsProps) {
  const start = totalCount === 0 ? 0 : (page - 1) * pageSize + 1
  const end = totalCount === 0 ? 0 : Math.min(page * pageSize, totalCount)
  const hasPrevious = page > 1
  const hasNext = totalPages > 0 && page < totalPages

  return (
    <div className="mt-4 flex flex-col gap-3 rounded-2xl border border-border/70 bg-background/35 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
        <span>
          {start}-{end} of {totalCount}
        </span>
        <label className="flex items-center gap-2">
          <span>Rows</span>
          <select
            className="rounded-lg border border-input bg-background px-2 py-1 text-sm text-foreground"
            value={pageSize}
            onChange={(event) => {
              onPageSizeChange(Number(event.target.value))
            }}
          >
            {pageSizeOptions.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="flex items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!hasPrevious}
          onClick={() => {
            onPageChange(page - 1)
          }}
        >
          Previous
        </Button>
        <span className="min-w-24 text-center text-sm text-muted-foreground">
          Page {totalPages === 0 ? 0 : page} of {totalPages}
        </span>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!hasNext}
          onClick={() => {
            onPageChange(page + 1)
          }}
        >
          Next
        </Button>
      </div>
    </div>
  )
}
