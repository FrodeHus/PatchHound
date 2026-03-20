import type { Column } from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import { cn } from '@/lib/utils'

type SortableColumnHeaderProps<TData, TValue> = {
  column: Column<TData, TValue>
  title: string
  className?: string
}

export function SortableColumnHeader<TData, TValue>({
  column,
  title,
  className,
}: SortableColumnHeaderProps<TData, TValue>) {
  if (!column.getCanSort()) {
    return <span className={className}>{title}</span>
  }

  const sorted = column.getIsSorted()

  return (
    <button
      type="button"
      className={cn(
        '-ml-2 flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium transition-colors hover:bg-muted/60',
        className,
      )}
      onClick={() => column.toggleSorting()}
    >
      {title}
      {sorted === 'asc' ? (
        <ArrowUp className="size-3 text-foreground" />
      ) : sorted === 'desc' ? (
        <ArrowDown className="size-3 text-foreground" />
      ) : (
        <ArrowUpDown className="size-3 text-muted-foreground/70" />
      )}
    </button>
  )
}
