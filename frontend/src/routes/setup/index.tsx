import { createFileRoute } from '@tanstack/react-router'
import { fetchSetupStatus } from '@/api/setup.functions'
import { SetupWizard } from '@/components/features/setup/SetupWizard'

export const Route = createFileRoute('/setup/')({
  loader: () => fetchSetupStatus(),
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
