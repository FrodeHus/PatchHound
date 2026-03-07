import { Link, createFileRoute } from '@tanstack/react-router'
import { Building2, ShieldCheck } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

export const Route = createFileRoute('/_authed/settings/')({
  component: SettingsPage,
})

function SettingsPage() {
  return (
    <section className="space-y-4 pb-4">
      <h1 className="text-2xl font-semibold">Settings</h1>
      <div className="grid gap-4 xl:grid-cols-2">
        <Link to="/admin/tenants" className="group">
          <Card className="rounded-[28px] border-border/70 bg-card/82 transition-colors group-hover:border-primary/35">
            <CardHeader>
              <div className="flex items-center justify-between">
                <CardTitle>Tenant Administration</CardTitle>
                <Building2 className="size-5 text-primary" />
              </div>
            </CardHeader>
            <CardContent className="text-sm leading-6 text-muted-foreground">
              Review configured tenants, rename them, and maintain ingestion credentials and sync schedules per source.
            </CardContent>
          </Card>
        </Link>

        <Card className="rounded-[28px] border-border/70 bg-card/70">
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle>Security Posture Settings</CardTitle>
              <ShieldCheck className="size-5 text-primary" />
            </div>
          </CardHeader>
          <CardContent className="text-sm leading-6 text-muted-foreground">
            Additional AI, SLA, and notification configuration can continue to live here as those controls are added.
          </CardContent>
        </Card>
      </div>
    </section>
  )
}
