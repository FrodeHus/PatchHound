import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Wrench, AlertTriangle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogClose,
} from '@/components/ui/dialog'
import { revokeAllRemediations } from '@/api/maintenance.functions'

export const Route = createFileRoute('/_authed/admin/maintenance')({
  component: MaintenancePage,
})

function MaintenancePage() {
  const { user } = Route.useRouteContext()
  const [confirmText, setConfirmText] = useState('')
  const [dialogOpen, setDialogOpen] = useState(false)

  const isGlobalAdmin = (user.activeRoles ?? []).includes('GlobalAdmin')

  const revokeMutation = useMutation({
    mutationFn: () => revokeAllRemediations(),
    onSuccess: () => {
      toast.success('All remediation data has been revoked.')
      setDialogOpen(false)
      setConfirmText('')
    },
    onError: () => {
      toast.error('Failed to revoke remediation data.')
    },
  })

  if (!isGlobalAdmin) {
    return (
      <section className="space-y-5">
        <div className="rounded-[32px] border border-border/70 bg-card/92 p-6">
          <p className="text-sm text-muted-foreground">
            You do not have permission to access this page. Only Global Admins can perform maintenance operations.
          </p>
        </div>
      </section>
    )
  }

  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Administration</p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">Maintenance</h1>
            <p className="max-w-2xl text-sm text-muted-foreground">
              Destructive operations for resetting tenant data. Use with caution.
            </p>
          </div>
          <div className="rounded-2xl border border-border/70 bg-background/30 p-4">
            <div className="flex items-center gap-3">
              <div className="rounded-2xl border border-border/70 bg-card/75 p-2">
                <Wrench className="size-5 text-primary" />
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="rounded-2xl border-border/70 bg-card/92">
          <CardHeader className="space-y-2">
            <div className="flex items-center gap-3">
              <div className="rounded-xl border border-destructive/20 bg-destructive/10 p-2">
                <AlertTriangle className="size-5 text-destructive" />
              </div>
              <CardTitle>Revoke All Remediations</CardTitle>
            </div>
            <p className="text-sm text-muted-foreground">
              Permanently delete all remediation decisions, approval tasks, patching tasks,
              analyst recommendations, and workflow data for the current tenant.
              This cannot be undone.
            </p>
          </CardHeader>
          <CardContent>
            <Dialog open={dialogOpen} onOpenChange={(open) => { setDialogOpen(open); if (!open) setConfirmText('') }}>
              <DialogTrigger render={<Button variant="destructive" />}>
                Revoke All Remediations
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>Revoke All Remediations</DialogTitle>
                  <DialogDescription>
                    This will permanently delete all remediation decisions, approval tasks,
                    patching tasks, analyst recommendations, and workflow data for the current tenant.
                    This action cannot be undone.
                  </DialogDescription>
                </DialogHeader>
                <div className="space-y-3 py-2">
                  <p className="text-sm text-muted-foreground">
                    Type <span className="font-mono font-semibold text-foreground">REVOKE</span> to confirm:
                  </p>
                  <input
                    type="text"
                    value={confirmText}
                    onChange={(e) => setConfirmText(e.target.value)}
                    placeholder="REVOKE"
                    className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
                    autoComplete="off"
                  />
                </div>
                <DialogFooter>
                  <DialogClose render={<Button variant="outline" />}>
                    Cancel
                  </DialogClose>
                  <Button
                    variant="destructive"
                    disabled={confirmText !== 'REVOKE' || revokeMutation.isPending}
                    onClick={() => revokeMutation.mutate()}
                  >
                    {revokeMutation.isPending ? 'Revoking...' : 'Revoke All'}
                  </Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>
          </CardContent>
        </Card>
      </div>
    </section>
  )
}
