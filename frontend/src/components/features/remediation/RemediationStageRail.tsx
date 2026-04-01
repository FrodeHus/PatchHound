import { Check, CircleDot, CircleOff, Clock3, X } from 'lucide-react'
import { cn } from '@/lib/utils'

export type RemediationStageId =
  | 'verification'
  | 'securityAnalysis'
  | 'remediationDecision'
  | 'approval'
  | 'execution'
  | 'closure'

export type RemediationStageState =
  | 'complete'
  | 'current'
  | 'pending'
  | 'skipped'
  | 'rejected'
  | 'closed'

export type RemediationStage = {
  id: RemediationStageId
  label: string
  state: RemediationStageState
  description: string
}

const stageStateCopy: Record<RemediationStageState, string> = {
  complete: 'Complete',
  current: 'Current',
  pending: 'Pending',
  skipped: 'Skipped',
  rejected: 'Rejected',
  closed: 'Closed',
}

export function RemediationStageRail({
  stages,
  currentStageId,
}: {
  stages: RemediationStage[]
  currentStageId: RemediationStageId
}) {
  const currentIndex = Math.max(
    stages.findIndex((stage) => stage.id === currentStageId),
    0,
  )
  const endpointInsetPercent = stages.length > 0 ? 50 / stages.length : 0

  return (
    <nav aria-label="Remediation workflow" className="overflow-x-auto pb-1">
        <ol
          className="relative grid min-w-[860px] gap-4 px-2"
          style={{ gridTemplateColumns: `repeat(${stages.length}, minmax(0, 1fr))` }}
        >
          <div
            className="pointer-events-none absolute top-5 h-[6px] rounded-full bg-muted/80"
            style={{ left: `${endpointInsetPercent}%`, right: `${endpointInsetPercent}%` }}
          />
          {stages.slice(0, -1).map((stage, index) => {
            const nextStage = stages[index + 1]
            const left = endpointInsetPercent + index * ((100 - endpointInsetPercent * 2) / Math.max(stages.length - 1, 1))
            const width = (100 - endpointInsetPercent * 2) / Math.max(stages.length - 1, 1)
            const regressionSegment =
              (stage.state === 'rejected' && nextStage?.state === 'current')
              || (stage.state === 'current' && nextStage?.state === 'rejected')
            const progressedSegment = index < currentIndex

            return (
              <div
                key={`${stage.id}-${nextStage.id}`}
                className={cn(
                  'pointer-events-none absolute transition-all duration-700 ease-out',
                  regressionSegment
                    ? 'top-[22px] h-0 border-t-2 border-dashed border-destructive bg-transparent'
                    : progressedSegment
                      ? 'top-5 h-[6px] rounded-full bg-[linear-gradient(90deg,color-mix(in_oklab,var(--primary)_72%,white),color-mix(in_oklab,var(--primary)_45%,transparent))]'
                      : 'top-5 h-[6px] bg-transparent',
                )}
                style={{
                  left: `${left}%`,
                  width: `${width}%`,
                }}
              />
            )
          })}

          {stages.map((stage) => (
            <li key={stage.id} className="relative flex flex-col items-center text-center">
              <div className="relative z-10 flex h-10 w-10 items-center justify-center rounded-full bg-background/95 shadow-sm ring-4 ring-background/90">
                {stage.state === 'current' ? (
                  <span className="pointer-events-none absolute inset-[-7px] rounded-full border border-primary/20 bg-primary/10 animate-pulse" />
                ) : null}
                <div
                  className={cn(
                    'relative flex h-10 w-10 items-center justify-center rounded-full border-2 transition-all duration-300',
                    stage.state === 'complete' && 'border-emerald-500 bg-emerald-500 text-white',
                    stage.state === 'closed' && 'border-teal-500 bg-teal-500 text-white',
                    stage.state === 'current' && 'border-primary bg-background text-primary shadow-[0_0_0_6px_color-mix(in_oklab,var(--primary)_18%,transparent)]',
                    stage.state === 'pending' && 'border-border/70 bg-background text-muted-foreground',
                    stage.state === 'skipped' && 'border-sky-300 bg-sky-500/8 text-sky-700 dark:text-sky-300',
                    stage.state === 'rejected' && 'border-destructive bg-destructive text-destructive-foreground',
                  )}
                >
                  {stage.state === 'current' ? (
                    <span className="pointer-events-none absolute inset-[5px] rounded-full bg-primary/8" />
                  ) : null}
                  <StageIcon state={stage.state} />
                </div>
              </div>

              <div
                className={cn(
                  'mt-4 w-full rounded-2xl px-2 py-2 transition-colors',
                  stage.state === 'current' && 'bg-primary/6',
                )}
              >
                <div className="flex min-h-10 flex-col items-center justify-start gap-1">
                  <p className="text-sm font-semibold text-foreground">{stage.label}</p>
                  <span
                    className={cn(
                      'inline-flex rounded-full border px-2 py-0.5 text-[10px] uppercase tracking-[0.16em] transition-colors duration-300',
                      stage.state === 'complete' && 'border-emerald-300/70 bg-emerald-500/8 text-emerald-700 dark:text-emerald-300',
                      stage.state === 'closed' && 'border-teal-300/70 bg-teal-500/8 text-teal-700 dark:text-teal-300',
                      stage.state === 'current' && 'border-primary/20 bg-primary/10 text-primary shadow-[0_0_18px_color-mix(in_oklab,var(--primary)_10%,transparent)]',
                      stage.state === 'pending' && 'border-border/70 bg-background text-muted-foreground',
                      stage.state === 'skipped' && 'border-sky-300/70 bg-sky-500/8 text-sky-700 dark:text-sky-300',
                      stage.state === 'rejected' && 'border-destructive/30 bg-destructive/8 text-destructive',
                    )}
                  >
                    {stageStateCopy[stage.state]}
                  </span>
                </div>
                <p className="mt-2 text-xs leading-relaxed text-muted-foreground">
                  {stage.description}
                </p>
              </div>
            </li>
          ))}
        </ol>
    </nav>
  )
}

function StageIcon({ state }: { state: RemediationStageState }) {
  switch (state) {
    case 'complete':
    case 'closed':
      return <Check className="size-4" />
    case 'current':
      return <CircleDot className="size-4" />
    case 'skipped':
      return <CircleOff className="size-4" />
    case 'rejected':
      return <X className="size-4" />
    default:
      return <Clock3 className="size-4" />
  }
}
