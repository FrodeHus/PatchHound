import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useEffect } from 'react'
import { useCompleteSetup, useSetupStatus } from '@/api/useSetup'
import { SetupWizard } from '@/components/features/setup/SetupWizard'

export const Route = createFileRoute('/setup/')({
  component: SetupPage,
})

function SetupPage() {
  const navigate = useNavigate()
  const statusQuery = useSetupStatus()
  const completeSetupMutation = useCompleteSetup()

  useEffect(() => {
    if (statusQuery.data?.isInitialized) {
      void navigate({ to: '/' })
    }
  }, [navigate, statusQuery.data?.isInitialized])

  if (statusQuery.isLoading) {
    return <p className="p-6 text-sm text-muted-foreground">Checking setup status...</p>
  }

  return (
    <div className="min-h-screen bg-background px-4">
      <SetupWizard
        isSubmitting={completeSetupMutation.isPending}
        onComplete={(payload) => {
          completeSetupMutation.mutate(payload, {
            onSuccess: () => {
              void navigate({ to: '/' })
            },
          })
        }}
      />
      {completeSetupMutation.isError ? (
        <p className="mx-auto mt-2 max-w-3xl text-sm text-destructive">Failed to complete setup.</p>
      ) : null}
    </div>
  )
}
