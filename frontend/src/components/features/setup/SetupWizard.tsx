import { useMemo, useState } from 'react'
import { ArrowRight } from 'lucide-react'
import type { SetupContext, SetupPayload } from '@/api/setup.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'

type SetupWizardProps = {
  setupContext: SetupContext
  isSubmitting: boolean
  onComplete: (payload: SetupPayload) => void
}

const steps = [
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
      ? !tenantValidationMessage
      : stepIndex === 1
        ? !defenderValidationMessage
        : true

  return (
    <section className="mx-auto w-full max-w-4xl px-6 py-10">
      <Card className="border-border/70 bg-card py-0 text-card-foreground shadow-[0_30px_80px_-35px_rgba(15,23,42,0.3)]">
        <CardHeader className="gap-4 border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_65%),color-mix(in_oklab,var(--card)_94%,transparent)] pb-5">
          <div className="space-y-2">
            <Badge
              variant="outline"
              className="rounded-full px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em]"
            >
              Tenant onboarding
            </Badge>
            <div className="space-y-1">
              <CardTitle className="text-2xl tracking-[-0.04em] text-foreground">
                Stand up a tenant in three short steps
              </CardTitle>
              <CardDescription className="text-sm leading-6 text-muted-foreground">
                Only onboarding fields are shown here. Admin identity and defaults are provisioned automatically.
              </CardDescription>
            </div>
          </div>

          <div className="flex flex-wrap gap-2">
            {steps.map((step, index) => {
              const isActive = index === stepIndex
              const isComplete = index < stepIndex
              const isAccessible = index <= stepIndex

              return (
                <button
                  key={step.id}
                  type="button"
                  disabled={!isAccessible}
                  className={cn(
                    'inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm transition-colors disabled:cursor-default disabled:opacity-100',
                    isActive
                      ? 'border-primary bg-primary text-primary-foreground'
                      : isComplete
                        ? 'border-emerald-500/25 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300'
                        : 'border-border bg-background text-muted-foreground',
                  )}
                  onClick={() => {
                    if (isAccessible) {
                      setStepIndex(index)
                    }
                  }}
                >
                  <span
                    className={cn(
                      'flex size-5 items-center justify-center rounded-full text-[11px] font-semibold',
                      isActive
                        ? 'bg-background text-foreground'
                        : isComplete
                          ? 'bg-emerald-600 text-white'
                          : 'bg-muted text-muted-foreground',
                    )}
                  >
                    {index + 1}
                  </span>
                  <span>{step.label}</span>
                </button>
              )
            })}
          </div>
        </CardHeader>

        <CardContent className="space-y-6 px-6 py-6 sm:px-8">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div className="space-y-1">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                {currentStep.label}
              </p>
              <h2 className="text-xl font-semibold tracking-[-0.03em] text-foreground">
                {currentStep.title}
              </h2>
              <p className="max-w-2xl text-sm leading-6 text-muted-foreground">{currentStep.description}</p>
            </div>

            <div className="rounded-2xl border border-border/70 bg-muted/40 px-4 py-3 text-sm">
              <p className="font-medium text-foreground">{setupContext.adminDisplayName}</p>
              <p className="text-muted-foreground">{setupContext.adminEmail}</p>
            </div>
          </div>

          {stepIndex === 0 ? (
            <div className="space-y-4">
              <div className="grid gap-2">
                <label className="text-sm font-medium text-foreground" htmlFor="tenant-name">
                  Workspace name
                </label>
                <Input
                  id="tenant-name"
                  value={tenantName}
                  onChange={(event) => {
                    setTenantName(event.target.value)
                  }}
                  placeholder="Acme Production"
                  className="h-11 rounded-xl px-3 text-sm"
                />
              </div>

              <div className="rounded-2xl border border-border/70 bg-muted/40 p-4 text-sm text-muted-foreground">
                Entra tenant: <span className="font-medium text-foreground">{setupContext.entraTenantId}</span>
              </div>

              {tenantValidationMessage ? (
                <p className="text-sm text-rose-600">{tenantValidationMessage}</p>
              ) : null}
            </div>
          ) : null}

          {stepIndex === 1 ? (
            <div className="space-y-4">
              <div className="flex items-center justify-between gap-4 rounded-2xl border border-border/70 bg-muted/40 p-4">
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
                <div className="grid gap-2">
                  <label className="text-sm font-medium text-foreground" htmlFor="defender-client-id">
                    Client ID
                  </label>
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
                </div>

                <div className="grid gap-2">
                  <label className="text-sm font-medium text-foreground" htmlFor="defender-client-secret">
                    Client secret
                  </label>
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
                </div>
              </div>

              <p className="text-sm text-muted-foreground">
                If enabled, PatchHound uses the default Defender schedule and your current Entra tenant ID.
              </p>

              {defenderValidationMessage ? (
                <p className="text-sm text-rose-600">{defenderValidationMessage}</p>
              ) : null}
            </div>
          ) : null}

          {stepIndex === 2 ? (
            <div className="space-y-4">
              <div className="rounded-2xl border border-border/70 bg-muted/40 p-4">
                <dl className="grid gap-4 text-sm sm:grid-cols-2">
                  <div>
                    <dt className="text-muted-foreground">Workspace name</dt>
                    <dd className="mt-1 font-medium text-foreground">{tenantName.trim()}</dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">Defender</dt>
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

          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <Button
              type="button"
              variant="outline"
              className="border-border bg-background text-foreground hover:bg-muted"
              disabled={stepIndex === 0 || isSubmitting}
              onClick={() => {
                setStepIndex((current) => Math.max(0, current - 1))
              }}
            >
              Back
            </Button>

            <div className="flex flex-col items-start gap-2 sm:items-end">
              {stepIndex === 1 && !defenderEnabled ? (
                <p className="text-sm text-muted-foreground">
                  You can set up Defender later from Sources.
                </p>
              ) : null}

              {stepIndex < steps.length - 1 ? (
                <Button
                  type="button"
                  disabled={!canContinue || isSubmitting}
                  onClick={() => {
                    setStepIndex((current) => Math.min(steps.length - 1, current + 1))
                  }}
                >
                  Continue
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
                  {isSubmitting ? 'Creating tenant...' : 'Create tenant'}
                </Button>
              )}
            </div>
          </div>
        </CardContent>
      </Card>
    </section>
  )
}
