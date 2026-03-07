import { createFileRoute, Outlet, redirect } from '@tanstack/react-router'
import { AppShell } from '@/components/layout/AppShell'

export const Route = createFileRoute('/_authed')({
  beforeLoad: async ({ context }) => {
    if (!context.user) {
      throw redirect({ to: '/auth/login' })
    }
    return { user: context.user }
  },
  component: AuthedLayout,
})

function AuthedLayout() {
  const { user } = Route.useRouteContext()
  return (
    <AppShell user={user}>
      <Outlet />
    </AppShell>
  )
}
