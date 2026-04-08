import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router'
import { toast } from 'sonner'
import { completeSetup, fetchSetupContext, fetchSetupStatus } from '@/api/setup.functions'
import { SetupAccessDialog } from '@/components/features/setup/SetupAccessDialog'
import { SetupHeader } from '@/components/features/setup/SetupHeader'
import { SetupWelcome } from '@/components/features/setup/SetupWelcome'
import { SetupWizard } from '@/components/features/setup/SetupWizard'

export const Route = createFileRoute('/setup/')({
  loader: async () => {
    const status = await fetchSetupStatus()
    if (!status.requiresSetup) {
      throw redirect({ to: '/dashboard' })
    }
    try {
      const setupContext = await fetchSetupContext()
      return { status, setupContext, setupError: null }
    } catch (error) {
      if (!(error instanceof Error)) {
        throw error
      }

      return {
        status,
        setupContext: null,
        setupError: error.message,
      }
    }
  },
  component: SetupPage,
})

function SetupPage() {
  const { setupContext, setupError } = Route.useLoaderData()
  const [started, setStarted] = useState(false)
  const router = useRouter()
  const mutation = useMutation({
    mutationFn: completeSetup,
    onSuccess: async () => {
      toast.success('Setup completed')
      await router.invalidate()
      await router.navigate({ to: '/dashboard' })
    },
    onError: () => {
      toast.error('Failed to complete setup')
    },
  })

  return (
    <section className="min-h-screen bg-[linear-gradient(180deg,color-mix(in_oklab,var(--background)_92%,var(--primary)_8%),var(--background))]">
      <SetupHeader
        onExit={started ? () => setStarted(false) : undefined}
      />

      {!started && setupContext ? (
        <SetupWelcome onStart={() => setStarted(true)} />
      ) : null}

      {started && setupContext ? (
        <SetupWizard
          setupContext={setupContext}
          isSubmitting={mutation.isPending}
          onComplete={(payload) => {
            mutation.mutate({ data: payload })
          }}
        />
      ) : null}

      {setupError ? <SetupAccessDialog message={setupError} /> : null}
    </section>
  )
}
