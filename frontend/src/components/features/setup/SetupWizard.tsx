import { useMemo, useState } from 'react'
import type { SetupPayload } from '@/api/setup.schemas'
import { AdminUserStep } from '@/components/features/setup/steps/AdminUserStep'
import { DefenderConnectionStep } from '@/components/features/setup/steps/DefenderConnectionStep'
import { EntraIdStep } from '@/components/features/setup/steps/EntraIdStep'
import { ReviewStep } from '@/components/features/setup/steps/ReviewStep'
import { SlaConfigStep } from '@/components/features/setup/steps/SlaConfigStep'
import { TenantConfigStep } from '@/components/features/setup/steps/TenantConfigStep'
import { WelcomeStep } from '@/components/features/setup/steps/WelcomeStep'

type SetupWizardProps = {
  isSubmitting: boolean
  onComplete: (payload: SetupPayload) => void
}

const steps = ['Welcome', 'Tenant', 'Entra ID', 'Defender', 'SLA', 'Admin', 'Review'] as const

export function SetupWizard({ isSubmitting, onComplete }: SetupWizardProps) {
  const [stepIndex, setStepIndex] = useState(0)
  const [tenantName, setTenantName] = useState('')
  const [entraTenantId, setEntraTenantId] = useState('')
  const [tenantSettings, setTenantSettings] = useState('{}')
  const [adminEmail, setAdminEmail] = useState('')
  const [adminDisplayName, setAdminDisplayName] = useState('')
  const [adminEntraObjectId, setAdminEntraObjectId] = useState('')

  const payload = useMemo<SetupPayload>(
    () => ({
      tenantName: tenantName.trim(),
      entraTenantId: entraTenantId.trim(),
      tenantSettings,
      adminEmail: adminEmail.trim(),
      adminDisplayName: adminDisplayName.trim(),
      adminEntraObjectId: adminEntraObjectId.trim(),
    }),
    [adminDisplayName, adminEmail, adminEntraObjectId, entraTenantId, tenantName, tenantSettings],
  )

  return (
    <section className="mx-auto max-w-3xl space-y-4 py-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold">PatchHound Setup Wizard</h1>
        <p className="text-sm text-muted-foreground">Step {stepIndex + 1} of {steps.length}: {steps[stepIndex]}</p>
      </header>

      {stepIndex === 0 ? <WelcomeStep /> : null}
      {stepIndex === 1 ? (
        <TenantConfigStep
          tenantName={tenantName}
          entraTenantId={entraTenantId}
          onTenantNameChange={setTenantName}
          onEntraTenantIdChange={setEntraTenantId}
        />
      ) : null}
      {stepIndex === 2 ? (
        <EntraIdStep tenantSettings={tenantSettings} onTenantSettingsChange={setTenantSettings} />
      ) : null}
      {stepIndex === 3 ? <DefenderConnectionStep /> : null}
      {stepIndex === 4 ? <SlaConfigStep /> : null}
      {stepIndex === 5 ? (
        <AdminUserStep
          adminEmail={adminEmail}
          adminDisplayName={adminDisplayName}
          adminEntraObjectId={adminEntraObjectId}
          onAdminEmailChange={setAdminEmail}
          onAdminDisplayNameChange={setAdminDisplayName}
          onAdminEntraObjectIdChange={setAdminEntraObjectId}
        />
      ) : null}
      {stepIndex === 6 ? <ReviewStep payload={payload} /> : null}

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
            disabled={
              isSubmitting
              || payload.tenantName.length === 0
              || payload.entraTenantId.length === 0
              || payload.adminEmail.length === 0
              || payload.adminDisplayName.length === 0
              || payload.adminEntraObjectId.length === 0
            }
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
