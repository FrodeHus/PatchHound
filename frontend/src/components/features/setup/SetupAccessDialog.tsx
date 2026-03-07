type SetupAccessDialogProps = {
  message: string
}

export function SetupAccessDialog({ message }: SetupAccessDialogProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/35 p-4">
      <div className="w-full max-w-lg rounded-2xl border border-border bg-background p-6 shadow-2xl">
        <div className="space-y-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-amber-600">
              Setup blocked
            </p>
            <h2 className="mt-2 text-2xl font-semibold tracking-[-0.03em]">
              Tenant.Admin is required
            </h2>
          </div>
          <p className="text-sm leading-6 text-muted-foreground">{message}</p>
          <div className="rounded-xl border border-amber-200 bg-amber-50/80 p-3 text-sm text-amber-900">
            Ask your Entra administrator to assign the <code>Tenant.Admin</code> app role to your
            user, then sign out and sign back in before retrying setup.
          </div>
        </div>
      </div>
    </div>
  )
}
