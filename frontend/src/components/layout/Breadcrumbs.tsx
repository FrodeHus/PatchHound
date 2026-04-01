import { useEffect } from 'react'
import { useMatches, useRouterState } from '@tanstack/react-router'
import { ChevronRight, Home } from 'lucide-react'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

const segmentLabels: Record<string, string> = {
  vulnerabilities: 'Vulnerabilities',
  assets: 'Assets',
  software: 'Software',
  tasks: 'Remediation',
  admin: 'Admin Console',
  platform: 'Platform Configuration',
  'audit-log': 'Audit Trail',
  'security-profiles': 'Security Profiles',
  users: 'Users',
  teams: 'Teams',
  sources: 'Sources',
  tenants: 'Tenants',
  'asset-rules': 'Asset Rules',
  ai: 'AI Settings',
  notifications: 'Notification Delivery',
  workflows: 'Workflows',
  integrations: 'Integrations',
  maintenance: 'Maintenance',
  'business-labels': 'Business Labels',
  changes: 'Changes',
  new: 'New',
}

const layoutRouteIds = new Set(['__root__', '/_authed'])

function isUuid(segment: string) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(segment)
}

/** Module-level cache of search params per pathname (survives re-renders, does not trigger them). */
const searchParamsCache = new Map<string, Record<string, unknown>>()

function isSearchParamPrimitive(value: unknown): value is string | number | boolean {
  return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
}

function buildHref(pathname: string, search: Record<string, unknown>) {
  const params = new URLSearchParams()

  for (const [key, value] of Object.entries(search)) {
    if (value === undefined || value === null || value === '') {
      continue
    }

    if (Array.isArray(value)) {
      for (const item of value) {
        if (item !== undefined && item !== null && item !== '' && isSearchParamPrimitive(item)) {
          params.append(key, String(item))
        }
      }
      continue
    }

    if (isSearchParamPrimitive(value)) {
      params.set(key, String(value))
    }
  }

  const query = params.toString()
  return query ? `${pathname}?${query}` : pathname
}

export function Breadcrumbs() {
  const matches = useMatches()
  const loaderData = useRouterState({ select: (state) => state.matches })

  // Snapshot search params for every non-layout match after each navigation
  useEffect(() => {
    for (const match of matches) {
      if (layoutRouteIds.has(match.routeId as string)) continue
      const search = match.search as Record<string, unknown> | undefined
      if (search && Object.keys(search).length > 0) {
        searchParamsCache.set(match.pathname.replace(/\/$/, ''), { ...search })
      }
    }
  }, [matches])

  const crumbs: { label: string; to: string; search: Record<string, unknown> }[] = []

  // Find the deepest non-layout match to build crumbs from its full path
  const leafMatch = [...matches].reverse().find((m) => !layoutRouteIds.has(m.routeId as string))
  if (!leafMatch) return null

  const pathSegments = leafMatch.pathname.replace(/\/$/, '').split('/').filter(Boolean)

  for (let i = 0; i < pathSegments.length; i++) {
    const segment = pathSegments[i]
    const to = '/' + pathSegments.slice(0, i + 1).join('/')

    if (crumbs.some((crumb) => crumb.to === to)) continue

    let label: string
    if (isUuid(segment)) {
      // Look for loader data from a matching route
      const routeMatch = loaderData.find((m) => m.pathname === to || m.pathname === to + '/')
      const loaderResult = routeMatch?.loaderData as Record<string, unknown> | undefined
      label = (loaderResult?.name as string)
        ?? (loaderResult?.title as string)
        ?? (loaderResult?.canonicalName as string)
        ?? (loaderResult?.externalId as string)
        ?? segment.slice(0, 8)
    } else {
      label = segmentLabels[segment] ?? segment.replace(/-/g, ' ').replace(/\b\w/g, (c: string) => c.toUpperCase())
    }

    crumbs.push({ label, to, search: searchParamsCache.get(to) ?? {} })
  }

  if (crumbs.length === 0) return null

  return (
    <nav aria-label="Breadcrumb" className="flex items-center gap-1 text-sm">
      <a href="/" className="text-muted-foreground/70 transition hover:text-foreground">
        <Home className="size-3.5" />
      </a>
      {crumbs.map((crumb, index) => {
        const isLast = index === crumbs.length - 1
        return (
          <span key={crumb.to} className="flex items-center gap-1">
            <ChevronRight className="size-3 text-muted-foreground/50" />
            {isLast ? (
              <Tooltip>
                <TooltipTrigger>
                  <span className="max-w-[200px] truncate font-medium text-foreground">{crumb.label}</span>
                </TooltipTrigger>
                <TooltipContent>{crumb.label}</TooltipContent>
              </Tooltip>
            ) : (
              <a href={buildHref(crumb.to, crumb.search)} className="text-muted-foreground/70 transition hover:text-foreground">
                {crumb.label}
              </a>
            )}
          </span>
        )
      })}
    </nav>
  )
}
