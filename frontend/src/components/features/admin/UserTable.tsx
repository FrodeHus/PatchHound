import type { UserItem } from '@/api/users.schemas'
import { ManageRolesDialog } from '@/components/features/admin/ManageRolesDialog'

type UserTableProps = {
  users: UserItem[]
  totalCount: number
  isUpdatingRoles: boolean
  tenants: Array<{ id: string; name: string }>
  onUpdateRoles: (userId: string, roles: Array<{ tenantId: string; role: string }>) => void
}

export function UserTable({ users, totalCount, isUpdatingRoles, tenants, onUpdateRoles }: UserTableProps) {
  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="mb-3 flex items-end justify-between">
        <h2 className="text-lg font-semibold">Users</h2>
        <p className="text-xs text-muted-foreground">{totalCount} total</p>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[980px] border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">Display Name</th>
              <th className="py-2 pr-2">Email</th>
              <th className="py-2 pr-2">Roles</th>
              <th className="py-2 pr-2">Manage Roles</th>
            </tr>
          </thead>
          <tbody>
            {users.length === 0 ? (
              <tr><td colSpan={4} className="py-3 text-muted-foreground">No users found.</td></tr>
            ) : (
              users.map((user) => (
                <tr key={user.id} className="border-b border-border/60">
                  <td className="py-2 pr-2 font-medium">{user.displayName}</td>
                  <td className="py-2 pr-2">{user.email}</td>
                  <td className="py-2 pr-2">
                    <div className="flex flex-wrap gap-1">
                      {user.roles.map((role) => (
                        <span key={`${role.tenantId}-${role.role}`} className="rounded bg-muted px-2 py-0.5 text-xs">
                          {role.tenantName}:{role.role}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="py-2 pr-2">
                    <ManageRolesDialog
                      userId={user.id}
                      isSubmitting={isUpdatingRoles}
                      tenants={tenants}
                      onUpdateRoles={onUpdateRoles}
                    />
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
