import { createFileRoute, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed/admin/users')({
  beforeLoad: () => {
    throw redirect({ to: '/admin/platform/access', search: { page: 1, pageSize: 25, search: '', role: '', status: '', teamId: '' }, replace: true })
  },
  component: () => null,
})
