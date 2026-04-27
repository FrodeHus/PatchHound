import { Link, createFileRoute } from '@tanstack/react-router'
import { ChevronRight } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { getAccessibleAdminSections } from '@/components/features/admin/admin-navigation'

export const Route = createFileRoute('/_authed/admin/')({
  component: AdminLandingPage,
})

function AdminLandingPage() {
  const { user } = Route.useRouteContext()
  const accessibleSections = getAccessibleAdminSections(user)
  const areaCount = accessibleSections.reduce((count, section) => count + section.areas.length, 0)

  return (
    <section className="space-y-5">
      <header className="rounded-2xl border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_58%),var(--color-card)] p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0 space-y-2">
            <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
              Admin console
            </p>
            <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">
              Configuration areas
            </h1>
            <p className="max-w-2xl text-sm leading-relaxed text-muted-foreground">
              Manage PatchHound tenant operations, automation, access, integrations,
              enrichment, and platform-level settings from one scannable control plane.
            </p>
          </div>
          <div className="rounded-xl border border-border/70 bg-background/50 px-3 py-2 text-right">
            <p className="text-2xl font-semibold tracking-tight">{areaCount}</p>
            <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
              available areas
            </p>
          </div>
        </div>
      </header>

      <div className="space-y-5">
        {accessibleSections.map((section) => (
          <section key={section.title} className="space-y-3">
            <div className="space-y-1 px-1">
              <h2 className="text-lg font-semibold tracking-tight">{section.title}</h2>
              <p className="text-sm text-muted-foreground">{section.description}</p>
            </div>
            <div className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
              {section.areas.map((area) => {
                const Icon = area.icon
                return (
                  <Link key={area.to} to={area.to} className="block">
                    <Card className="h-full rounded-2xl border-border/70 bg-card/72 shadow-none transition hover:border-primary/30 hover:bg-muted/35">
                      <CardHeader className="pb-3">
                        <div className="flex items-start justify-between gap-3">
                          <div className="flex min-w-0 items-center gap-3">
                            <span className="flex size-10 shrink-0 items-center justify-center rounded-xl border border-border/70 bg-background/55 text-primary">
                              <Icon className="size-5" />
                            </span>
                            <CardTitle className="truncate text-base">{area.title}</CardTitle>
                          </div>
                          <ChevronRight className="mt-2 size-4 shrink-0 text-muted-foreground" />
                        </div>
                      </CardHeader>
                      <CardContent>
                        <p className="text-sm leading-relaxed text-muted-foreground">
                          {area.description}
                        </p>
                      </CardContent>
                    </Card>
                  </Link>
                )
              })}
            </div>
          </section>
        ))}
      </div>
    </section>
  )
}
