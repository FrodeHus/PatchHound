import { LogOut } from 'lucide-react'
import { Button } from '@/components/ui/button'

type SetupHeaderProps = {
  onExit?: () => void
}

export function SetupHeader({ onExit }: SetupHeaderProps) {
  return (
    <header className="sticky top-0 z-50 flex h-14 items-center justify-between border-b border-border/40 bg-background/80 px-6 backdrop-blur-md">
      <span className="text-sm font-bold uppercase tracking-[0.2em] text-foreground">
        PatchHound
      </span>

      {onExit ? (
        <Button variant="ghost" size="sm" onClick={onExit}>
          <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
            Exit onboarding
          </span>
          <LogOut className="size-3.5 text-muted-foreground" />
        </Button>
      ) : null}
    </header>
  )
}
