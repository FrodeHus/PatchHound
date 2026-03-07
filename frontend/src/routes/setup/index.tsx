import { createFileRoute, redirect } from '@tanstack/react-router'
import { fetchSetupStatus } from '@/api/setup.functions'
import { SetupWizard } from '@/components/features/setup/SetupWizard'

export const Route = createFileRoute('/setup/')({
  loader: async () => {
    const status = await fetchSetupStatus()
    if (status.isInitialized) {
      throw redirect({ to: '/' })
    }
    return status
  },
  component: SetupPage,
})

function SetupPage() {
  const status = Route.useLoaderData()

  return (
    <section className="flex min-h-screen items-center justify-center">
      <SetupWizard status={status} />
    </section>
  )
}
