import { cn } from '@/lib/utils'

type SkeletonProps = React.ComponentProps<'div'>

export function Skeleton({ className, ...props }: SkeletonProps) {
  return (
    <div
      className={cn('animate-pulse rounded-2xl bg-muted/60', className)}
      {...props}
    />
  )
}

export function TableSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="space-y-2">
      <Skeleton className="h-10 w-full rounded-xl" />
      {Array.from({ length: rows }).map((_, i) => (
        <Skeleton key={i} className="h-12 w-full rounded-xl" />
      ))}
    </div>
  )
}

export function CardSkeleton() {
  return (
    <div className="space-y-3 rounded-3xl border border-border/70 p-5">
      <Skeleton className="h-4 w-1/3" />
      <Skeleton className="h-8 w-1/4" />
      <Skeleton className="h-24 w-full" />
    </div>
  )
}

export function StatSkeleton() {
  return (
    <div className="space-y-3 rounded-2xl border border-border/70 p-4">
      <Skeleton className="h-3 w-2/5" />
      <Skeleton className="h-10 w-1/3" />
    </div>
  )
}
