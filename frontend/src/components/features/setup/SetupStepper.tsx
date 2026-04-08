import { Check } from 'lucide-react'
import { cn } from '@/lib/utils'

type SetupStepperProps = {
  steps: ReadonlyArray<{ id: string; label: string }>
  currentIndex: number
  onStepClick?: (index: number) => void
}

export function SetupStepper({ steps, currentIndex, onStepClick }: SetupStepperProps) {
  return (
    <nav aria-label="Setup progress" className="flex items-center gap-0">
      {steps.map((step, index) => {
        const isActive = index === currentIndex
        const isComplete = index < currentIndex
        const isAccessible = index <= currentIndex

        return (
          <div key={step.id} className="flex items-center">
            {/* Connector line (before every step except the first) */}
            {index > 0 ? (
              <div
                className={cn(
                  'h-px w-8 sm:w-12',
                  isComplete ? 'bg-primary' : 'bg-border',
                )}
              />
            ) : null}

            {/* Step circle */}
            <button
              type="button"
              disabled={!isAccessible}
              onClick={() => isAccessible && onStepClick?.(index)}
              aria-current={isActive ? 'step' : undefined}
              className={cn(
                'flex size-8 shrink-0 items-center justify-center rounded-full text-xs font-semibold transition-colors',
                'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background',
                'disabled:cursor-default',
                isComplete
                  ? 'bg-primary text-primary-foreground'
                  : isActive
                    ? 'border-2 border-primary bg-background text-primary'
                    : 'border border-border bg-background text-muted-foreground',
              )}
            >
              {isComplete ? <Check className="size-3.5" /> : index + 1}
            </button>
          </div>
        )
      })}
    </nav>
  )
}
