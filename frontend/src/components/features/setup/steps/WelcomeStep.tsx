export function WelcomeStep() {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-xl font-semibold">Welcome to PatchHound</h2>
      <p className="text-sm text-muted-foreground">
        This wizard configures your first tenant from Entra and assigns the logged-in user as the initial Global Admin.
      </p>
    </section>
  )
}
