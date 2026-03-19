import { Link, useMatches, useRouterState } from '@tanstack/react-router'
import { ChevronRight, Home } from 'lucide-react'

const segmentLabels: Record<string, string> = {
  vulnerabilities: 'Vulnerabilities',
  assets: 'Assets',
  software: 'Software',
  tasks: 'Remediation',
  settings: 'Settings',
  admin: 'Admin Console',
  'audit-log': 'Audit Trail',
  'security-profiles': 'Security Profiles',
  users: 'Users',
  teams: 'Teams',
  sources: 'Sources',
  tenants: 'Tenants',
  'asset-rules': 'Asset Rules',
  ai: 'AI Settings',
  changes: 'Changes',
  new: 'New',
}

const layoutRouteIds = new Set(['__root__', '/_authed'])

function isUuid(segment: string) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(segment)
}

export function Breadcrumbs() {
  const matches = useMatches()
  const loaderData = useRouterState({ select: (state) => state.matches })

  const crumbs: { label: string; to: string }[] = []

  for (const match of matches) {
    const routeId = match.routeId as string
    if (layoutRouteIds.has(routeId)) continue

    const pathSegments = match.pathname.replace(/\/$/, '').split('/').filter(Boolean)
    const lastSegment = pathSegments[pathSegments.length - 1]
    if (!lastSegment) continue

    if (crumbs.some((crumb) => crumb.to === match.pathname)) continue

    let label: string
    if (isUuid(lastSegment)) {
      const data = loaderData.find((m) => m.routeId === routeId)
      const loaderResult = data?.loaderData as Record<string, unknown> | undefined
      label = (loaderResult?.name as string)
        ?? (loaderResult?.title as string)
        ?? (loaderResult?.canonicalName as string)
        ?? (loaderResult?.externalId as string)
        ?? lastSegment.slice(0, 8)
    } else {
      label = segmentLabels[lastSegment] ?? lastSegment.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
    }

    crumbs.push({ label, to: match.pathname })
  }

  if (crumbs.length === 0) return null

  return (
    <nav aria-label="Breadcrumb" className="flex items-center gap-1 text-sm">
      {/* eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment */}
      <Link to={'/' as any} search={{} as any} className="text-muted-foreground/70 transition hover:text-foreground">
        <Home className="size-3.5" />
      </Link>
      {crumbs.map((crumb, index) => {
        const isLast = index === crumbs.length - 1
        return (
          <span key={crumb.to} className="flex items-center gap-1">
            <ChevronRight className="size-3 text-muted-foreground/50" />
            {isLast ? (
              <span className="max-w-[200px] truncate font-medium text-foreground">{crumb.label}</span>
            ) : (
              // eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-unsafe-assignment
              <Link to={crumb.to as any} search={{} as any} className="text-muted-foreground/70 transition hover:text-foreground">
                {crumb.label}
              </Link>
            )}
          </span>
        )
      })}
    </nav>
  )
}
