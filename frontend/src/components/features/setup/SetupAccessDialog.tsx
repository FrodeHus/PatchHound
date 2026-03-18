import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { toneBadge, toneText } from '@/lib/tone-classes'

type SetupAccessDialogProps = {
  message: string
}

export function SetupAccessDialog({ message }: SetupAccessDialogProps) {
  return (
    <Dialog open>
      <DialogContent showCloseButton={false} className="w-full max-w-lg rounded-2xl border-border/80 bg-card p-6 sm:max-w-lg">
        <div className="space-y-3">
          <DialogHeader className="p-0">
            <p className={`text-xs font-semibold uppercase tracking-[0.2em] ${toneText('warning')}`}>
              Setup blocked
            </p>
            <DialogTitle className="mt-2 text-2xl font-semibold tracking-[-0.03em]">
              Tenant.Admin is required
            </DialogTitle>
          </DialogHeader>
          <p className="text-sm leading-6 text-muted-foreground">{message}</p>
          <div className={`rounded-xl border p-3 text-sm ${toneBadge('warning')}`}>
            Ask your Entra administrator to assign the <code>Tenant.Admin</code> app role to your
            user, then sign out and sign back in before retrying setup.
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
