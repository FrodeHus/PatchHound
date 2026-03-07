import { useState } from 'react'

type ManageRolesDialogProps = {
  userId: string
  isSubmitting: boolean
  tenants: Array<{ id: string; name: string }>
  onUpdateRoles: (userId: string, roles: Array<{ tenantId: string; role: string }>) => void
}

const roleOptions = ['GlobalAdmin', 'SecurityManager', 'SecurityAnalyst', 'AssetOwner', 'Stakeholder', 'Auditor']

export function ManageRolesDialog({ userId, isSubmitting, tenants, onUpdateRoles }: ManageRolesDialogProps) {
  const [tenantId, setTenantId] = useState(tenants[0]?.id ?? '')
  const [role, setRole] = useState(roleOptions[0])

  return (
    <div className="flex flex-wrap items-center gap-2">
      <select
        className="rounded-md border border-input bg-background px-2 py-1 text-xs"
        value={tenantId}
        onChange={(event) => {
          setTenantId(event.target.value)
        }}
      >
        {tenants.map((tenant) => (
          <option key={tenant.id} value={tenant.id}>
            {tenant.name}
          </option>
        ))}
      </select>
      <select
        className="rounded-md border border-input bg-background px-2 py-1 text-xs"
        value={role}
        onChange={(event) => {
          setRole(event.target.value)
        }}
      >
        {roleOptions.map((option) => (
          <option key={option} value={option}>{option}</option>
        ))}
      </select>
      <button
        type="button"
        className="rounded-md border border-input px-2 py-1 text-xs hover:bg-muted disabled:opacity-50"
        disabled={isSubmitting || tenantId.trim().length === 0}
        onClick={() => {
          onUpdateRoles(userId, [{ tenantId: tenantId.trim(), role }])
          setTenantId(tenants[0]?.id ?? '')
        }}
      >
        {isSubmitting ? 'Saving...' : 'Assign role'}
      </button>
    </div>
  )
}
