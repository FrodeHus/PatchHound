import { useState } from 'react'
import type { SetupContext, SetupPayload } from '@/api/setup.schemas'
import { AdminUserStep } from '@/components/features/setup/steps/AdminUserStep'
import { DefenderConnectionStep } from '@/components/features/setup/steps/DefenderConnectionStep'
import { ReviewStep } from '@/components/features/setup/steps/ReviewStep'
import { SlaConfigStep } from '@/components/features/setup/steps/SlaConfigStep'
import { TenantConfigStep } from '@/components/features/setup/steps/TenantConfigStep'
import { WelcomeStep } from '@/components/features/setup/steps/WelcomeStep'

type SetupWizardProps = {
  setupContext: SetupContext
  isSubmitting: boolean
  onComplete: (payload: SetupPayload) => void
}

const steps = ['Welcome', 'Tenant', 'Defender', 'SLA', 'Admin', 'Review'] as const

export function SetupWizard({ setupContext, isSubmitting, onComplete }: SetupWizardProps) {
  const [stepIndex, setStepIndex] = useState(0)
  const payload: SetupPayload = {}

  return (
    <section className="mx-auto max-w-3xl space-y-4 py-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold">PatchHound Setup Wizard</h1>
        <p className="text-sm text-muted-foreground">Step {stepIndex + 1} of {steps.length}: {steps[stepIndex]}</p>
      </header>

      {stepIndex === 0 ? <WelcomeStep /> : null}
      {stepIndex === 1 ? (
        <TenantConfigStep setupContext={setupContext} />
      ) : null}
      {stepIndex === 2 ? <DefenderConnectionStep /> : null}
      {stepIndex === 3 ? <SlaConfigStep /> : null}
      {stepIndex === 4 ? <AdminUserStep setupContext={setupContext} /> : null}
      {stepIndex === 5 ? <ReviewStep setupContext={setupContext} /> : null}

      <footer className="flex items-center justify-between">
        <button
          type="button"
          className="rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted disabled:opacity-50"
          disabled={stepIndex === 0}
          onClick={() => {
            setStepIndex((current) => Math.max(0, current - 1))
          }}
        >
          Back
        </button>

        {stepIndex < steps.length - 1 ? (
          <button
            type="button"
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
            onClick={() => {
              setStepIndex((current) => Math.min(steps.length - 1, current + 1))
            }}
          >
            Next
          </button>
        ) : (
          <button
            type="button"
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
            disabled={isSubmitting}
            onClick={() => {
              onComplete(payload)
            }}
          >
            {isSubmitting ? 'Completing setup...' : 'Complete setup'}
          </button>
        )}
      </footer>
    </section>
  )
}
