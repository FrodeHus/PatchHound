import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/assets/')({
  component: AssetsPage,
})

function AssetsPage() {
  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">Assets</h1>
      <p className="text-sm text-muted-foreground">Asset ownership and criticality management will be added in later tasks.</p>
    </section>
  )
}
