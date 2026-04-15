import { createFileRoute, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed/admin/advanced-tools')({
  beforeLoad: () => {
    throw redirect({ to: '/admin/platform/advanced-tools', replace: true })
  },
  component: () => null,
})
