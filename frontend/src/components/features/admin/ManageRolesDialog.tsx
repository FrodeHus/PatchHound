import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { roleOptions } from '@/lib/options/roles'

type ManageRolesDialogProps = {
  userId: string
  isSubmitting: boolean
  tenants: Array<{ id: string; name: string }>
  onUpdateRoles: (userId: string, roles: Array<{ tenantId: string; role: string }>) => void
}

export function ManageRolesDialog({ userId, isSubmitting, tenants, onUpdateRoles }: ManageRolesDialogProps) {
  const [tenantId, setTenantId] = useState(tenants[0]?.id ?? '')
  const [role, setRole] = useState<(typeof roleOptions)[number]>(roleOptions[0])

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Select
        value={tenantId}
        onValueChange={(value) => {
          if (value) {
            setTenantId(value)
          }
        }}
      >
        <SelectTrigger className="h-7 min-w-36 rounded-md bg-background px-2 text-xs">
          <SelectValue placeholder="Select tenant" />
        </SelectTrigger>
        <SelectContent>
          {tenants.map((tenant) => (
            <SelectItem key={tenant.id} value={tenant.id}>
              {tenant.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Select
        value={role}
        onValueChange={(value) => {
          if (value) {
            setRole(value)
          }
        }}
      >
        <SelectTrigger className="h-7 min-w-32 rounded-md bg-background px-2 text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {roleOptions.map((option) => (
            <SelectItem key={option} value={option}>
              {option}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        type="button"
        variant="outline"
        size="xs"
        disabled={isSubmitting || tenantId.trim().length === 0}
        onClick={() => {
          onUpdateRoles(userId, [{ tenantId: tenantId.trim(), role }])
          setTenantId(tenants[0]?.id ?? '')
        }}
      >
        {isSubmitting ? 'Saving...' : 'Assign role'}
      </Button>
    </div>
  )
}
