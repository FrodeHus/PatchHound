import type { ReactNode } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { cn } from '@/lib/utils'

type WorkbenchFilterDrawerProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  activeCount: number
  onResetDraft: () => void
  onApply: () => void
  children: ReactNode
}

export function WorkbenchFilterDrawer({
  open,
  onOpenChange,
  title,
  description,
  activeCount,
  onResetDraft,
  onApply,
  children,
}: WorkbenchFilterDrawerProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="w-full overflow-hidden border-l border-border/80 bg-card p-0 sm:max-w-xl"
      >
        <SheetHeader className="gap-3 border-b border-border/70 bg-background/70">
          <div className="flex items-start justify-between gap-3 pr-10">
            <div className="space-y-1">
              <SheetTitle>{title}</SheetTitle>
              <SheetDescription>{description}</SheetDescription>
            </div>
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
              {activeCount} active
            </Badge>
          </div>
          <div className="flex items-center gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={onResetDraft}>
              Clear all
            </Button>
          </div>
        </SheetHeader>

        <div className="flex-1 overflow-y-auto px-5 py-5">
          <div className="space-y-5">{children}</div>
        </div>

        <SheetFooter className="border-t border-border/70 bg-background/70 sm:flex-row sm:justify-end">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="button" onClick={onApply}>
            Apply filters
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  )
}

export function WorkbenchFilterSection({
  title,
  description,
  children,
  className,
}: {
  title: string
  description?: string
  children: ReactNode
  className?: string
}) {
  return (
    <section className={cn('space-y-3 rounded-2xl border border-border/70 bg-background/45 p-4', className)}>
      <div className="space-y-1">
        <h3 className="text-sm font-semibold tracking-tight">{title}</h3>
        {description ? <p className="text-xs text-muted-foreground">{description}</p> : null}
      </div>
      <div className="space-y-3">{children}</div>
    </section>
  )
}
