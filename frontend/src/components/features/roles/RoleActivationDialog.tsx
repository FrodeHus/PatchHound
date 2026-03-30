import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRouter } from '@tanstack/react-router'
import { toast } from 'sonner'
import { activateRoles } from '@/api/roles.functions'
import type { CurrentUser } from '@/server/auth.functions'

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Switch } from '@/components/ui/switch'

const ROLE_DISPLAY_NAMES: Record<string, string> = {
  CustomerAdmin: 'Customer Admin',
  CustomerOperator: 'Customer Operator',
  CustomerViewer: 'Customer Viewer',
  SecurityManager: 'Security Manager',
  SecurityAnalyst: 'Security Analyst',
  AssetOwner: 'Asset Owner',
  TechnicalManager: 'Technical Manager',
  GlobalAdmin: 'Global Admin',
  Auditor: 'Auditor',
  Stakeholder: 'Stakeholder',
}

type RoleActivationDialogProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  user: CurrentUser
}

export function RoleActivationDialog({
  open,
  onOpenChange,
  user,
}: RoleActivationDialogProps) {
  const router = useRouter()
  const queryClient = useQueryClient()
  const [pendingRole, setPendingRole] = useState<string | null>(null)

  const elevatedRoles = user.roles.filter((role) => role !== 'Stakeholder')
  const activeRoles = new Set(user.activeRoles ?? [])

  const mutation = useMutation({
    mutationFn: (roles: string[]) => activateRoles({ data: { roles } }),
    onSuccess: async () => {
      await router.invalidate()
      await queryClient.invalidateQueries()
      setPendingRole(null)
    },
    onError: (error) => {
      toast.error(
        error instanceof Error ? error.message : 'Failed to update roles',
      )
      setPendingRole(null)
    },
  })

  function handleToggle(role: string, checked: boolean) {
    setPendingRole(role)
    const newRoles = checked
      ? [...activeRoles, role]
      : [...activeRoles].filter((r) => r !== role)

    const displayName = ROLE_DISPLAY_NAMES[role] ?? role

    mutation.mutate(newRoles, {
      onSuccess: () => {
        toast.success(
          checked
            ? `${displayName} activated`
            : `${displayName} deactivated`,
        )
      },
    })
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Activate Roles</DialogTitle>
          <DialogDescription>
            Elevated roles grant additional permissions. Active roles reset when
            you log out.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-2">
          {/* Stakeholder — always active */}
          <div className="flex items-center justify-between rounded-lg bg-muted/40 px-4 py-3">
            <div>
              <p className="text-sm font-medium">Stakeholder</p>
              <p className="text-xs text-muted-foreground">Always active</p>
            </div>
            <Switch checked disabled aria-label="Stakeholder role (always active)" />
          </div>

          {/* Assigned elevated roles */}
          {elevatedRoles.length === 0 ? (
            <p className="px-4 py-3 text-sm text-muted-foreground">
              No additional roles are assigned to your account. Contact your
              administrator to request role access.
            </p>
          ) : (
            elevatedRoles.map((role) => {
              const isActive = activeRoles.has(role)
              const isPending = pendingRole === role && mutation.isPending
              const displayName = ROLE_DISPLAY_NAMES[role] ?? role

              return (
                <div
                  key={role}
                  className="flex items-center justify-between rounded-lg border border-border/50 px-4 py-3"
                >
                  <p className="text-sm font-medium">{displayName}</p>
                  <Switch
                    checked={isActive}
                    disabled={isPending}
                    onCheckedChange={(checked) => handleToggle(role, checked)}
                    aria-label={`${displayName} role`}
                  />
                </div>
              )
            })
          )}
        </div>

        <p className="text-center text-xs text-muted-foreground">
          Active roles reset when you log out
        </p>
      </DialogContent>
    </Dialog>
  )
}
