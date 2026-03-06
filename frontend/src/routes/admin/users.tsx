import { createFileRoute } from '@tanstack/react-router'
import { InviteUserDialog } from '@/components/features/admin/InviteUserDialog'
import { UserTable } from '@/components/features/admin/UserTable'
import { useInviteUser, useUpdateUserRoles, useUsers } from '@/api/useUsers'

export const Route = createFileRoute('/admin/users')({
  component: AdminUsersPage,
})

function AdminUsersPage() {
  const usersQuery = useUsers(1, 100)
  const inviteMutation = useInviteUser()
  const updateRolesMutation = useUpdateUserRoles()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">User Management</h1>

      <InviteUserDialog
        isSubmitting={inviteMutation.isPending}
        onInvite={(payload) => {
          inviteMutation.mutate(payload)
        }}
      />

      {usersQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading users...</p> : null}
      {usersQuery.isError ? <p className="text-sm text-destructive">Failed to load users.</p> : null}

      {usersQuery.data ? (
        <UserTable
          users={usersQuery.data.items}
          totalCount={usersQuery.data.totalCount}
          isUpdatingRoles={updateRolesMutation.isPending}
          onUpdateRoles={(userId, roles) => {
            updateRolesMutation.mutate({ userId, roles })
          }}
        />
      ) : null}
    </section>
  )
}
