import { useState } from 'react'

type ManageRolesDialogProps = {
  userId: string
  isSubmitting: boolean
  onUpdateRoles: (userId: string, roles: Array<{ tenantId: string; role: string }>) => void
}

const roleOptions = ['GlobalAdmin', 'SecurityManager', 'SecurityAnalyst', 'AssetOwner', 'Stakeholder', 'Auditor']

export function ManageRolesDialog({ userId, isSubmitting, onUpdateRoles }: ManageRolesDialogProps) {
  const [tenantId, setTenantId] = useState('')
  const [role, setRole] = useState(roleOptions[0])

  return (
    <div className="flex flex-wrap items-center gap-2">
      <input
        className="rounded-md border border-input bg-background px-2 py-1 text-xs"
        placeholder="Tenant GUID"
        value={tenantId}
        onChange={(event) => {
          setTenantId(event.target.value)
        }}
      />
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
          setTenantId('')
        }}
      >
        {isSubmitting ? 'Saving...' : 'Assign role'}
      </button>
    </div>
  )
}
