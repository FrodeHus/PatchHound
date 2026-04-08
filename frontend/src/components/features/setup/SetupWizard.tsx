import { useMemo, useState } from 'react'
import { ArrowLeft, ArrowRight, ArrowUpRight, CircleHelp } from 'lucide-react'
import type { SetupContext, SetupPayload } from '@/api/setup.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import { SetupStepper } from './SetupStepper'
import { SetupStepSidebar } from './SetupStepSidebar'

type SetupWizardProps = {
  setupContext: SetupContext
  isSubmitting: boolean
  onComplete: (payload: SetupPayload) => void
}

const steps = [
  {
    id: 'entra',
    label: 'Entra app',
    title: 'Onboard the PatchHound Entra application',
    description: 'Grant tenant-wide admin consent to the configured multi-tenant app before completing workspace setup.',
  },
  {
    id: 'workspace',
    label: 'Workspace',
    title: 'Create the tenant workspace',
    description: 'Set the tenant name operators will see in PatchHound.',
  },
  {
    id: 'defender',
    label: 'Defender',
    title: 'Optional Defender setup',
    description: 'Add Defender credentials now or skip and configure them later from Sources.',
  },
  {
    id: 'review',
    label: 'Review',
    title: 'Review and launch',
    description: 'Confirm the onboarding choices before PatchHound creates the tenant.',
  },
] as const

export function SetupWizard({ setupContext, isSubmitting, onComplete }: SetupWizardProps) {
  const [stepIndex, setStepIndex] = useState(0)
  const [tenantName, setTenantName] = useState(setupContext.tenantName)
  const [defenderEnabled, setDefenderEnabled] = useState(false)
  const [defenderClientId, setDefenderClientId] = useState('')
  const [defenderClientSecret, setDefenderClientSecret] = useState('')

  const currentStep = steps[stepIndex]
  const isReviewStep = stepIndex === 3
  const showSidebar = !isReviewStep

  const tenantValidationMessage = tenantName.trim()
    ? null
    : 'Workspace name is required.'

  const defenderValidationMessage = useMemo(() => {
    if (!defenderEnabled) {
      return null
    }

    if (!defenderClientId.trim()) {
      return 'Client ID is required when Defender setup is enabled.'
    }

    if (!defenderClientSecret.trim()) {
      return 'Client secret is required when Defender setup is enabled.'
    }

    return null
  }, [defenderClientId, defenderClientSecret, defenderEnabled])

  const canContinue =
    stepIndex === 0
      ? true
      : stepIndex === 1
        ? !tenantValidationMessage
        : stepIndex === 2
        ? !defenderValidationMessage
        : true

  return (
    <TooltipProvider>
      <section className="mx-auto w-full max-w-5xl px-6 py-10">
        {/* Stepper */}
        <div className="mb-8">
          <SetupStepper
            steps={steps}
            currentIndex={stepIndex}
            onStepClick={(index) => setStepIndex(index)}
          />
        </div>

        {/* Step header */}
        <div className="mb-8 space-y-2">
          <h2 className="text-2xl font-semibold tracking-[-0.04em] text-foreground">
            {currentStep.title}
          </h2>
          <p className="max-w-2xl text-sm leading-6 text-muted-foreground">
            {currentStep.description}
          </p>
        </div>

        {/* Two-column layout: form + sidebar (single column on review) */}
        <div className={cn(
          'gap-8',
          showSidebar ? 'grid lg:grid-cols-[1fr_320px]' : '',
        )}>
          {/* Main content column */}
          <div className="space-y-6">
            {stepIndex === 0 ? (
              <div className="space-y-4">
                <div className="rounded-2xl border border-border/70 bg-muted/50 p-4">
                  <p className="text-sm leading-6 text-muted-foreground">
                    PatchHound uses a multi-tenant Microsoft Entra application for sign-in. Before this tenant can use it, an Entra administrator must onboard the application into the tenant by granting admin consent to the app registration configured for this deployment.
                  </p>
                </div>

                <div className="grid gap-4 md:grid-cols-2">
                  <div className="rounded-2xl border border-border/70 bg-background p-4">
                    <p className="text-sm font-medium text-foreground">What this consent covers</p>
                    <ul className="mt-3 space-y-2 text-sm leading-6 text-muted-foreground">
                      <li>Standard delegated sign-in permissions: `openid`, `profile`, `email`, and `offline_access`.</li>
                      <li>`Microsoft Graph` delegated permission: `User.Read` for basic signed-in user profile access.</li>
                      <li>If this same Entra app is reused for Defender ingestion, the consent will also approve these `WindowsDefenderATP` application permissions: `Machine.Read.All`, `Score.Read.All`, `Software.Read.All`, and `Vulnerability.Read.All`.</li>
                    </ul>
                  </div>

                  <div className="rounded-2xl border border-border/70 bg-background p-4">
                    <p className="text-sm font-medium text-foreground">Configured application</p>
                    <dl className="mt-3 space-y-3 text-sm">
                      <div>
                        <dt className="text-muted-foreground">Entra tenant</dt>
                        <dd className="mt-1 font-medium text-foreground break-all">{setupContext.entraTenantId}</dd>
                      </div>
                      <div>
                        <dt className="text-muted-foreground">Client ID</dt>
                        <dd className="mt-1 font-medium text-foreground break-all">
                          {setupContext.appClientId || 'Not configured'}
                        </dd>
                      </div>
                    </dl>
                    <div className="mt-4">
                      {setupContext.adminConsentUrl ? (
                        <Button
                          render={(
                            <a
                              href={setupContext.adminConsentUrl}
                              target="_blank"
                              rel="noreferrer"
                              title="Grant admin consent in Microsoft Entra"
                            />
                          )}
                        >
                          Grant admin consent
                          <ArrowUpRight className="size-4" />
                        </Button>
                      ) : (
                        <Button type="button" disabled>
                          Grant admin consent
                        </Button>
                      )}
                    </div>
                    <p className="mt-3 text-sm leading-6 text-muted-foreground">
                      This opens Microsoft Entra in a new tab for tenant-wide consent. After consent is granted, return here and continue setup.
                    </p>
                  </div>
                </div>
              </div>
            ) : null}

            {stepIndex === 1 ? (
              <div className="space-y-4">
                <Field
                  label="Tenant name"
                  htmlFor="tenant-name"
                  tooltip="The tenant display name operators will see throughout PatchHound."
                  control={(
                    <Input
                      id="tenant-name"
                      value={tenantName}
                      onChange={(event) => {
                        setTenantName(event.target.value)
                      }}
                      placeholder="e.g., Global Security Corp"
                      className="h-11 rounded-xl px-3 text-sm"
                    />
                  )}
                />

                <Field
                  label="Primary admin email"
                  htmlFor="admin-email"
                  control={(
                    <Input
                      id="admin-email"
                      value={setupContext.adminEmail}
                      disabled
                      className="h-11 rounded-xl px-3 text-sm"
                    />
                  )}
                />

                <div className="rounded-2xl border border-border/70 bg-muted/50 p-4 text-sm text-muted-foreground">
                  Entra tenant: <span className="font-medium text-foreground">{setupContext.entraTenantId}</span>
                </div>

                {tenantValidationMessage ? (
                  <p className="text-sm text-rose-600">{tenantValidationMessage}</p>
                ) : null}
              </div>
            ) : null}

            {stepIndex === 2 ? (
              <div className="space-y-4">
                <div className="flex items-center justify-between gap-4 rounded-2xl border border-border/70 bg-muted/50 p-4">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <p className="font-medium text-foreground">Set up Microsoft Defender now</p>
                      <Badge variant="outline" className="rounded-full">
                        Optional
                      </Badge>
                    </div>
                    <p className="text-sm leading-6 text-muted-foreground">
                      Skip this if you want to configure the source later from Sources.
                    </p>
                  </div>

                  <Checkbox
                    checked={defenderEnabled}
                    onCheckedChange={(checked) => {
                      setDefenderEnabled(Boolean(checked))
                    }}
                    aria-label="Enable Defender during onboarding"
                  />
                </div>

                <div className="grid gap-4 md:grid-cols-2">
                  <Field
                    label="Client ID"
                    htmlFor="defender-client-id"
                    tooltip="Application client identifier used for the Defender ingestion source."
                    control={(
                      <Input
                        id="defender-client-id"
                        value={defenderClientId}
                        onChange={(event) => {
                          const value = event.target.value
                          setDefenderClientId(value)
                          if (value.trim()) {
                            setDefenderEnabled(true)
                          }
                        }}
                        placeholder="Application (client) ID"
                        className="h-11 rounded-xl px-3 text-sm"
                      />
                    )}
                  />

                  <Field
                    label="Client secret"
                    htmlFor="defender-client-secret"
                    tooltip="Secret value for the Defender application. Stored server-side after setup."
                    control={(
                      <Input
                        id="defender-client-secret"
                        type="password"
                        value={defenderClientSecret}
                        onChange={(event) => {
                          const value = event.target.value
                          setDefenderClientSecret(value)
                          if (value.trim()) {
                            setDefenderEnabled(true)
                          }
                        }}
                        placeholder="Paste the secret value"
                        className="h-11 rounded-xl px-3 text-sm"
                      />
                    )}
                  />
                </div>

                {defenderValidationMessage ? (
                  <p className="text-sm text-rose-600">{defenderValidationMessage}</p>
                ) : null}
              </div>
            ) : null}

            {stepIndex === 3 ? (
              <div className="space-y-4">
                <div className="rounded-2xl border border-border/70 bg-muted/50 p-4">
                  <dl className="grid gap-4 text-sm sm:grid-cols-2">
                    <div>
                      <dt className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        Workspace name
                      </dt>
                      <dd className="mt-1 font-medium text-foreground">{tenantName.trim()}</dd>
                    </div>
                    <div>
                      <dt className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        Primary admin
                      </dt>
                      <dd className="mt-1 font-medium text-foreground">{setupContext.adminDisplayName}</dd>
                      <dd className="text-muted-foreground">{setupContext.adminEmail}</dd>
                    </div>
                    <div>
                      <dt className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        Entra application
                      </dt>
                      <dd className="mt-1 font-medium text-foreground break-all">{setupContext.appClientId || 'Not configured'}</dd>
                    </div>
                    <div>
                      <dt className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                        Defender
                      </dt>
                      <dd className="mt-1 font-medium text-foreground">
                        {defenderEnabled ? 'Configure on launch' : 'Skip for now'}
                      </dd>
                    </div>
                  </dl>
                </div>

                <p className="text-sm text-muted-foreground">
                  PatchHound will still create the initial admin assignment, default SLA, and baseline source records automatically.
                </p>
              </div>
            ) : null}

            <Separator className="bg-border/70" />

            {/* Navigation */}
            <div className="flex items-center justify-between">
              <Button
                type="button"
                variant="ghost"
                disabled={stepIndex === 0 || isSubmitting}
                onClick={() => {
                  setStepIndex((current) => Math.max(0, current - 1))
                }}
              >
                <ArrowLeft className="size-4" />
                Back
              </Button>

              {stepIndex < steps.length - 1 ? (
                <Button
                  type="button"
                  disabled={!canContinue || isSubmitting}
                  onClick={() => {
                    setStepIndex((current) => Math.min(steps.length - 1, current + 1))
                  }}
                >
                  Save and Continue
                  <ArrowRight className="size-4" />
                </Button>
              ) : (
                <Button
                  type="button"
                  disabled={isSubmitting || !tenantName.trim() || !!defenderValidationMessage}
                  onClick={() => {
                    onComplete({
                      tenantName: tenantName.trim(),
                      defender: {
                        enabled: defenderEnabled,
                        clientId: defenderClientId.trim(),
                        clientSecret: defenderClientSecret,
                      },
                    })
                  }}
                >
                  {isSubmitting ? 'Creating tenant...' : 'Complete Setup'}
                  {!isSubmitting ? <ArrowRight className="size-4" /> : null}
                </Button>
              )}
            </div>
          </div>

          {/* Contextual sidebar */}
          {showSidebar ? (
            <div className="hidden lg:block">
              <div className="sticky top-24">
                <SetupStepSidebar stepId={currentStep.id} />
              </div>
            </div>
          ) : null}
        </div>
      </section>
    </TooltipProvider>
  )
}

function Field({
  label,
  htmlFor,
  tooltip,
  control,
}: {
  label: string
  htmlFor: string
  tooltip?: string
  control: React.ReactNode
}) {
  return (
    <div className="grid content-start gap-2">
      <div className="flex min-h-5 items-center gap-2">
        <label className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground" htmlFor={htmlFor}>
          {label}
        </label>
        {tooltip ? (
          <Tooltip>
            <TooltipTrigger className="inline-flex items-center text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
              <CircleHelp className="size-3.5" />
            </TooltipTrigger>
            <TooltipContent
              align="start"
              className="max-w-sm rounded-lg border border-border/80 bg-popover px-3 py-2 text-xs leading-5 text-popover-foreground shadow-lg"
            >
              {tooltip}
            </TooltipContent>
          </Tooltip>
        ) : null}
      </div>
      {control}
    </div>
  )
}
