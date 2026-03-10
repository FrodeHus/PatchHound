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
    <section className="min-h-screen bg-[radial-gradient(circle_at_top_left,_rgba(56,189,248,0.14),_transparent_32%),radial-gradient(circle_at_bottom_right,_rgba(16,185,129,0.12),_transparent_28%),linear-gradient(180deg,_rgba(248,250,252,1),_rgba(241,245,249,0.92))]">
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
