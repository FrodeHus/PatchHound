import { createFileRoute, Outlet, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed')({
  beforeLoad: async ({ context }) => {
    if (!context.user) {
      throw redirect({ to: '/auth/login' })
    }
    return { user: context.user }
  },
  component: () => <Outlet />,
})
