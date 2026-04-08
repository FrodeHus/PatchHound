import { ArrowRight, Globe, Radar, ShieldCheck, Workflow } from 'lucide-react'
import { Button } from '@/components/ui/button'

type SetupWelcomeProps = {
  onStart: () => void
}

const pillars = [
  {
    icon: Radar,
    title: 'Discovery',
    description: 'Map your infrastructure and identify active nodes.',
  },
  {
    icon: ShieldCheck,
    title: 'Vulnerability',
    description: 'Sync with global threat databases in real-time.',
  },
  {
    icon: Workflow,
    title: 'Orchestration',
    description: 'Define automated patching windows and policies.',
  },
] as const

export function SetupWelcome({ onStart }: SetupWelcomeProps) {
  return (
    <div className="flex min-h-[calc(100vh-3.5rem)] flex-col items-center justify-center px-6 py-20">
      <div className="flex flex-col items-center gap-10 text-center">
        {/* Shield icon */}
        <div className="flex size-20 items-center justify-center rounded-2xl border border-border/50 bg-card shadow-lg shadow-primary/5">
          <ShieldCheck className="size-10 text-primary" />
        </div>

        {/* Hero text */}
        <div className="space-y-4">
          <h1 className="text-4xl font-bold tracking-[-0.04em] text-foreground sm:text-5xl">
            Welcome to{' '}
            <span className="text-primary">PatchHound</span>
          </h1>
          <p className="mx-auto max-w-xl text-base leading-7 text-muted-foreground sm:text-lg">
            This wizard will set up the foundation for continuous
            patch orchestration across your entire ecosystem.
          </p>
        </div>

        {/* Three pillars */}
        <div className="grid w-full max-w-2xl grid-cols-1 gap-6 sm:grid-cols-3">
          {pillars.map((pillar) => (
            <div key={pillar.title} className="flex flex-col items-center gap-3 text-center">
              <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
                <pillar.icon className="size-5" />
              </div>
              <div className="space-y-1">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-foreground">
                  {pillar.title}
                </p>
                <p className="text-sm leading-5 text-muted-foreground">
                  {pillar.description}
                </p>
              </div>
            </div>
          ))}
        </div>

        {/* CTA */}
        <div className="flex flex-col items-center gap-3">
          <Button size="lg" className="h-12 gap-2 rounded-xl px-8 text-base" onClick={onStart}>
            Start Configuration
            <ArrowRight className="size-4" />
          </Button>
          <p className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
            <span className="mr-1.5 inline-block size-1.5 rounded-full bg-tone-success" />
            Estimated setup: 5 minutes
          </p>
        </div>
      </div>

      {/* Decorative globe hint — bottom right */}
      <div className="pointer-events-none fixed bottom-0 right-0 hidden opacity-20 lg:block">
        <Globe className="size-64 text-primary" strokeWidth={0.5} />
      </div>
    </div>
  )
}
