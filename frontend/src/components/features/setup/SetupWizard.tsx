import { useMemo, useState } from 'react'
import { ArrowRight, CheckCircle2, Shield, Sparkles } from 'lucide-react'
import type { SetupContext, SetupPayload } from '@/api/setup.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import {
  Progress,
  ProgressLabel,
} from '@/components/ui/progress'
import { Separator } from '@/components/ui/separator'

type SetupWizardProps = {
  setupContext: SetupContext
  isSubmitting: boolean
  onComplete: (payload: SetupPayload) => void
}

const steps = [
  {
    id: 'workspace',
    eyebrow: 'Step 1',
    title: 'Name the tenant workspace',
    description: 'Set the name your operators will see across PatchHound.',
  },
  {
    id: 'defender',
    eyebrow: 'Step 2',
    title: 'Optional Defender connection',
    description: 'Add credentials now, or skip this and configure Defender later from Sources.',
  },
  {
    id: 'review',
    eyebrow: 'Step 3',
    title: 'Review and launch',
    description: 'Confirm the onboarding scope before PatchHound creates the tenant workspace.',
  },
] as const

export function SetupWizard({ setupContext, isSubmitting, onComplete }: SetupWizardProps) {
  const [stepIndex, setStepIndex] = useState(0)
  const [tenantName, setTenantName] = useState(setupContext.tenantName)
  const [defenderEnabled, setDefenderEnabled] = useState(false)
  const [defenderClientId, setDefenderClientId] = useState('')
  const [defenderClientSecret, setDefenderClientSecret] = useState('')

  const progress = ((stepIndex + 1) / steps.length) * 100
  const currentStep = steps[stepIndex]

  const defenderValidationMessage = useMemo(() => {
    if (!defenderEnabled) {
      return null
    }

    if (!defenderClientId.trim()) {
      return 'Client ID is required to enable Defender during onboarding.'
    }

    if (!defenderClientSecret.trim()) {
      return 'Client secret is required to enable Defender during onboarding.'
    }

    return null
  }, [defenderClientId, defenderClientSecret, defenderEnabled])

  const tenantValidationMessage = tenantName.trim()
    ? null
    : 'Workspace name is required to continue.'

  const canContinue =
    stepIndex === 0
      ? !tenantValidationMessage
      : stepIndex === 1
        ? !defenderValidationMessage
        : true

  return (
    <section className="mx-auto flex min-h-screen w-full max-w-6xl items-center px-6 py-10">
      <div className="grid w-full gap-6 lg:grid-cols-[280px_minmax(0,1fr)]">
        <Card className="border-border/70 bg-[radial-gradient(circle_at_top,_rgba(59,130,246,0.08),_transparent_55%),linear-gradient(180deg,_rgba(255,255,255,0.9),_rgba(248,250,252,0.96))] shadow-sm">
          <CardHeader>
            <div className="space-y-3">
              <Badge variant="outline" className="w-fit rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em]">
                Tenant onboarding
              </Badge>
              <div className="space-y-1">
                <CardTitle className="text-xl tracking-[-0.03em]">
                  Stand up a tenant in one pass
                </CardTitle>
                <CardDescription className="text-sm leading-6">
                  PatchHound will create the tenant workspace, assign the first admin, and optionally
                  prepare Microsoft Defender ingestion.
                </CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-5">
            <Progress value={progress}>
              <div className="flex w-full items-end justify-between">
                <ProgressLabel>Onboarding progress</ProgressLabel>
                <span className="ml-auto text-sm text-muted-foreground tabular-nums">
                  {stepIndex + 1} / {steps.length}
                </span>
              </div>
            </Progress>

            <div className="space-y-3">
              {steps.map((step, index) => {
                const state =
                  index < stepIndex ? 'done' : index === stepIndex ? 'active' : 'upcoming'

                return (
                  <button
                    key={step.id}
                    type="button"
                    className={`w-full rounded-2xl border px-4 py-3 text-left transition-colors ${
                      state === 'active'
                        ? 'border-foreground/15 bg-background shadow-sm'
                        : state === 'done'
                          ? 'border-emerald-200 bg-emerald-50/60'
                          : 'border-transparent bg-muted/45 hover:border-border/60'
                    }`}
                    onClick={() => {
                      if (index <= stepIndex) {
                        setStepIndex(index)
                      }
                    }}
                  >
                    <div className="flex items-start gap-3">
                      <div
                        className={`mt-0.5 flex size-7 items-center justify-center rounded-full text-xs font-semibold ${
                          state === 'done'
                            ? 'bg-emerald-600 text-white'
                            : state === 'active'
                              ? 'bg-foreground text-background'
                              : 'bg-background text-muted-foreground ring-1 ring-border'
                        }`}
                      >
                        {state === 'done' ? <CheckCircle2 className="size-4" /> : index + 1}
                      </div>
                      <div className="min-w-0 space-y-1">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                          {step.eyebrow}
                        </p>
                        <p className="font-medium tracking-[-0.02em]">{step.title}</p>
                        <p className="text-sm leading-5 text-muted-foreground">{step.description}</p>
                      </div>
                    </div>
                  </button>
                )
              })}
            </div>

            <div className="rounded-2xl border border-dashed border-border/70 bg-background/70 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                Included automatically
              </p>
              <ul className="mt-3 space-y-2 text-sm text-muted-foreground">
                <li className="flex items-start gap-2">
                  <Sparkles className="mt-0.5 size-4 text-primary" />
                  The first admin account is created from your current Entra sign-in.
                </li>
                <li className="flex items-start gap-2">
                  <Shield className="mt-0.5 size-4 text-primary" />
                  Default SLA and enrichment defaults are provisioned without extra fields.
                </li>
              </ul>
            </div>
          </CardContent>
        </Card>

        <Card className="overflow-hidden border-border/70 shadow-sm">
          <CardHeader className="border-b bg-[linear-gradient(180deg,_rgba(15,23,42,0.03),_transparent)] pb-5">
            <div className="space-y-2">
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                {currentStep.eyebrow}
              </p>
              <CardTitle className="text-3xl tracking-[-0.04em]">{currentStep.title}</CardTitle>
              <CardDescription className="max-w-2xl text-sm leading-6">
                {currentStep.description}
              </CardDescription>
            </div>
          </CardHeader>

          <CardContent className="space-y-6 py-6">
            {stepIndex === 0 ? (
              <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_260px]">
                <div className="space-y-4">
                  <div className="space-y-2">
                    <label className="text-sm font-medium" htmlFor="tenant-name">
                      Tenant workspace name
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
                    <p className="text-sm text-muted-foreground">
                      This is the only editable tenant field during onboarding. Everything else is
                      derived from your current identity.
                    </p>
                    {tenantValidationMessage ? (
                      <p className="text-sm text-destructive">{tenantValidationMessage}</p>
                    ) : null}
                  </div>
                </div>

                <div className="rounded-2xl border bg-muted/35 p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                    Identity source
                  </p>
                  <dl className="mt-3 space-y-3 text-sm">
                    <div>
                      <dt className="text-muted-foreground">Entra tenant</dt>
                      <dd className="font-medium">{setupContext.entraTenantId}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground">Initial admin</dt>
                      <dd className="font-medium">{setupContext.adminDisplayName}</dd>
                      <dd className="text-muted-foreground">{setupContext.adminEmail}</dd>
                    </div>
                  </dl>
                </div>
              </div>
            ) : null}

            {stepIndex === 1 ? (
              <div className="space-y-5">
                <div className="flex items-start justify-between gap-4 rounded-2xl border bg-muted/30 p-4">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <p className="font-medium tracking-[-0.02em]">Configure Microsoft Defender now</p>
                      <Badge variant="outline" className="rounded-full">Optional</Badge>
                    </div>
                    <p className="text-sm leading-6 text-muted-foreground">
                      If you skip this, the tenant will still be created and you can configure
                      Defender later from <span className="font-medium text-foreground">Sources</span>.
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

                <div className={`grid gap-4 transition-opacity ${defenderEnabled ? 'opacity-100' : 'opacity-60'}`}>
                  <div className="grid gap-2">
                    <label className="text-sm font-medium" htmlFor="defender-client-id">
                      Client ID
                    </label>
                    <Input
                      id="defender-client-id"
                      value={defenderClientId}
                      disabled={!defenderEnabled}
                      onChange={(event) => {
                        setDefenderClientId(event.target.value)
                      }}
                      placeholder="Application (client) ID"
                      className="h-11 rounded-xl px-3 text-sm"
                    />
                  </div>

                  <div className="grid gap-2">
                    <label className="text-sm font-medium" htmlFor="defender-client-secret">
                      Client secret
                    </label>
                    <Input
                      id="defender-client-secret"
                      type="password"
                      value={defenderClientSecret}
                      disabled={!defenderEnabled}
                      onChange={(event) => {
                        setDefenderClientSecret(event.target.value)
                      }}
                      placeholder="Paste the secret value"
                      className="h-11 rounded-xl px-3 text-sm"
                    />
                  </div>
                </div>

                <div className="rounded-2xl border border-dashed border-border/70 bg-background p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                    Applied automatically if enabled
                  </p>
                  <ul className="mt-3 space-y-2 text-sm text-muted-foreground">
                    <li>PatchHound will keep the default Defender schedule and token scope.</li>
                    <li>Your current Entra tenant ID will be used as the credential tenant.</li>
                    <li>You can revisit and rotate these credentials later from tenant settings.</li>
                  </ul>
                </div>

                {defenderValidationMessage ? (
                  <p className="text-sm text-destructive">{defenderValidationMessage}</p>
                ) : null}
              </div>
            ) : null}

            {stepIndex === 2 ? (
              <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_280px]">
                <div className="space-y-5">
                  <div className="rounded-2xl border bg-muted/25 p-4">
                    <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                      Tenant workspace
                    </p>
                    <p className="mt-2 text-lg font-medium tracking-[-0.03em]">{tenantName.trim()}</p>
                    <p className="mt-2 text-sm text-muted-foreground">
                      The first Global Admin will be {setupContext.adminDisplayName} ({setupContext.adminEmail}).
                    </p>
                  </div>

                  <Separator />

                  <div className="rounded-2xl border bg-muted/25 p-4">
                    <div className="flex items-center gap-2">
                      <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                        Defender ingestion
                      </p>
                      {defenderEnabled ? (
                        <Badge className="rounded-full">Will be configured</Badge>
                      ) : (
                        <Badge variant="outline" className="rounded-full">Skipped for now</Badge>
                      )}
                    </div>
                    <p className="mt-2 text-sm text-muted-foreground">
                      {defenderEnabled
                        ? 'The default Microsoft Defender source will be enabled immediately after tenant creation.'
                        : 'The tenant will launch without Defender credentials. You can configure the source later from Sources.'}
                    </p>
                  </div>
                </div>

                <div className="rounded-2xl border bg-[linear-gradient(180deg,_rgba(15,23,42,0.04),_transparent)] p-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                    Launch result
                  </p>
                  <ul className="mt-3 space-y-3 text-sm text-muted-foreground">
                    <li>Tenant workspace is created and linked to your Entra tenant.</li>
                    <li>Default SLA and enrichment sources are provisioned automatically.</li>
                    <li>You land in the main app immediately after setup completes.</li>
                  </ul>
                </div>
              </div>
            ) : null}
          </CardContent>

          <CardFooter className="justify-between gap-3">
            <Button
              type="button"
              variant="outline"
              disabled={stepIndex === 0 || isSubmitting}
              onClick={() => {
                setStepIndex((current) => Math.max(0, current - 1))
              }}
            >
              Back
            </Button>

            <div className="flex items-center gap-3">
              {stepIndex === 1 && !defenderEnabled ? (
                <span className="text-sm text-muted-foreground">
                  Skipping Defender is fine. You can configure it later.
                </span>
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
                  {isSubmitting ? 'Creating tenant...' : 'Create tenant workspace'}
                </Button>
              )}
            </div>
          </CardFooter>
        </Card>
      </div>
    </section>
  )
}
