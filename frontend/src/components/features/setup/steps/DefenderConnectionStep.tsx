export function DefenderConnectionStep() {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Defender Connection</h2>
      <p className="text-sm text-muted-foreground">
        Defender credentials are managed via environment variables. Verify they are set in deployment.
      </p>
    </section>
  )
}
