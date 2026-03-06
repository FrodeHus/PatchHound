import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/vulnerabilities/')({
  component: VulnerabilitiesPage,
})

function VulnerabilitiesPage() {
  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">Vulnerabilities</h1>
      <p className="text-sm text-muted-foreground">List and filtering UI will be added in the next task.</p>
    </section>
  )
}
