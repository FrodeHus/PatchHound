import { useMutation } from '@tanstack/react-query'
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router'
import { completeSetup, fetchSetupContext, fetchSetupStatus } from '@/api/setup.functions'
import { SetupAccessDialog } from '@/components/features/setup/SetupAccessDialog'
import { SetupWizard } from '@/components/features/setup/SetupWizard'

export const Route = createFileRoute('/setup/')({
  loader: async () => {
    const status = await fetchSetupStatus()
    if (!status.requiresSetup) {
      throw redirect({ to: '/' })
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
  const router = useRouter()
  const mutation = useMutation({
    mutationFn: completeSetup,
    onSuccess: () => {
      void router.navigate({ to: '/' })
    },
  })

  return (
    <section className="flex min-h-screen items-center justify-center">
      {setupContext ? (
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
