import { createFileRoute, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed/admin/integrations/')({
  beforeLoad: () => {
    throw redirect({ to: '/admin/platform/integrations', replace: true })
  },
  component: () => null,
})
