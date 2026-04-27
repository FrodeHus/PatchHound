import { Link, useRouterState } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { Menu, ShieldCheck } from 'lucide-react'
import type { CurrentUser } from '@/server/auth.functions'
import { Button } from '@/components/ui/button'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/components/ui/sheet'
import { cn } from '@/lib/utils'
import {
  getAccessibleAdminSections,
  type AdminArea,
  type AdminSection,
} from '@/components/features/admin/admin-navigation'

type AdminConsoleLayoutProps = {
  user: CurrentUser
  children: ReactNode
}

export function AdminConsoleLayout({ user, children }: AdminConsoleLayoutProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const accessibleSections = getAccessibleAdminSections(user)

  return (
    <section className="space-y-4 py-1">
      <div className="flex items-center justify-between gap-3 xl:hidden">
        <div className="min-w-0">
          <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
            Admin console
          </p>
          <h1 className="truncate text-xl font-semibold tracking-tight">Configuration</h1>
        </div>
        <Sheet>
          <SheetTrigger
            render={
              <Button type="button" variant="outline" className="gap-2 rounded-xl" />
            }
          >
            <Menu className="size-4" />
            Areas
          </SheetTrigger>
          <SheetContent side="left" className="w-[21rem] bg-card p-0 sm:max-w-[21rem]">
            <SheetHeader className="border-b border-border/70">
              <SheetTitle>Admin areas</SheetTitle>
              <SheetDescription>Move between PatchHound configuration surfaces.</SheetDescription>
            </SheetHeader>
            <div className="min-h-0 overflow-y-auto p-3">
              <AdminNavigation sections={accessibleSections} pathname={pathname} />
            </div>
          </SheetContent>
        </Sheet>
      </div>

      <div className="grid gap-5 xl:grid-cols-[18rem_minmax(0,1fr)]">
        <aside className="sticky top-28 hidden max-h-[calc(100dvh-8rem)] overflow-hidden rounded-2xl border border-border/70 bg-card/72 xl:block">
          <div className="border-b border-border/70 p-4">
            <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
              Admin console
            </p>
            <h2 className="mt-1 text-lg font-semibold tracking-tight">Configuration</h2>
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
              Tenant, automation, and platform controls.
            </p>
          </div>
          <div className="max-h-[calc(100dvh-15rem)] overflow-y-auto p-3 [scrollbar-width:thin] [scrollbar-color:color-mix(in_oklab,var(--muted-foreground)_34%,transparent)_transparent]">
            <AdminNavigation sections={accessibleSections} pathname={pathname} />
          </div>
        </aside>

        <div className="min-w-0">{children}</div>
      </div>
    </section>
  )
}

function AdminNavigation({
  sections,
  pathname,
}: {
  sections: AdminSection[]
  pathname: string
}) {
  return (
    <nav className="space-y-5" aria-label="Admin navigation">
      <AdminNavigationLink
        area={{
          title: 'Overview',
          description: 'All admin areas grouped by operating domain.',
          to: '/admin',
          roles: ['Stakeholder'],
          icon: ShieldCheck,
        }}
        isActive={pathname === '/admin' || pathname === '/admin/'}
      />

      {sections.map((section) => (
        <section key={section.title} className="space-y-1.5">
          <div className="px-2">
            <p className="text-[10px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
              {section.title}
            </p>
            <p className="mt-0.5 text-[11px] leading-snug text-muted-foreground/80">
              {section.description}
            </p>
          </div>
          <div className="space-y-1">
            {section.areas.map((area) => (
              <AdminNavigationLink
                key={area.to}
                area={area}
                isActive={pathname === area.to || pathname.startsWith(`${area.to}/`)}
              />
            ))}
          </div>
        </section>
      ))}
    </nav>
  )
}

function AdminNavigationLink({
  area,
  isActive,
}: {
  area: AdminArea
  isActive: boolean
}) {
  const Icon = area.icon

  return (
    <Link
      to={area.to}
      className={cn(
        'group flex min-h-11 items-center gap-3 rounded-xl border border-transparent px-3 py-2.5 text-sm text-muted-foreground transition-colors hover:border-border/70 hover:bg-muted/45 hover:text-foreground',
        isActive
          && 'border-primary/20 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_14%,transparent),transparent_78%),var(--color-background)] text-foreground shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]',
      )}
    >
      <span
        className={cn(
          'flex size-8 shrink-0 items-center justify-center rounded-lg border border-border/65 bg-background/55 text-muted-foreground transition-colors group-hover:text-primary',
          isActive && 'border-primary/24 bg-primary/12 text-primary',
        )}
      >
        <Icon className="size-4" />
      </span>
      <span className="min-w-0 flex-1 truncate tracking-tight">{area.title}</span>
    </Link>
  )
}
