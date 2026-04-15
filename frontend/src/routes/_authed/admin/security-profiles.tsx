import { createFileRoute, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed/admin/security-profiles')({
  beforeLoad: () => {
    throw redirect({ to: '/admin/platform/security-profiles', search: { page: 1, pageSize: 25 }, replace: true })
  },
  component: () => null,
})
