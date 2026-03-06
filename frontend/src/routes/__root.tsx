import { createRootRoute, Outlet, useNavigate, useRouterState } from '@tanstack/react-router'
import { useEffect } from 'react'
import { useSetupStatus } from '@/api/useSetup'
import { AppShell } from '@/components/layout/AppShell'

export const Route = createRootRoute({
  component: RootLayout,
})

function RootLayout() {
  const navigate = useNavigate()
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const setupStatusQuery = useSetupStatus()
  const isSetupRoute = pathname.startsWith('/setup')
  const isInitialized = setupStatusQuery.data?.isInitialized

  useEffect(() => {
    if (setupStatusQuery.isLoading || setupStatusQuery.isError || isInitialized === undefined) {
      return
    }

    if (!isInitialized && !isSetupRoute) {
      void navigate({ to: '/setup' })
      return
    }

    if (isInitialized && isSetupRoute) {
      void navigate({ to: '/' })
    }
  }, [isInitialized, isSetupRoute, navigate, setupStatusQuery.isError, setupStatusQuery.isLoading])

  if (isSetupRoute) {
    return <Outlet />
  }

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  )
}
