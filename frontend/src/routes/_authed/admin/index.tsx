import { Link, createFileRoute } from '@tanstack/react-router'
import { Bell, Bot, Braces, Building2, ChevronRight, DatabaseZap, Flag, GitBranchPlus, Globe, Plug, ScanSearch, ShieldCheck, ShieldEllipsis, Tags, Users, Workflow, Wrench } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

export const Route = createFileRoute('/_authed/admin/')({
  component: AdminLandingPage,
})

type AdminRole =
  | 'GlobalAdmin'
  | 'CustomerAdmin'
  | 'CustomerOperator'
  | 'CustomerViewer'
  | 'SecurityManager'
  | 'SecurityAnalyst'
  | 'AssetOwner'
  | 'TechnicalManager'
  | 'Auditor'
  | 'Stakeholder'

type AdminArea = {
  title: string
  description: string
  to:
    | '/admin/teams'
    | '/admin/tenants'
    | '/admin/sources'
    | '/admin/business-labels'
    | '/admin/device-rules'
    | '/admin/workflows'
    | '/admin/maintenance'
    | '/admin/authenticated-scans'
    | '/admin/platform/ai'
    | '/admin/platform/notifications'
    | '/admin/platform/feature-flags'
    | '/admin/platform/integrations'
    | '/admin/platform/advanced-tools'
    | '/admin/platform/access'
    | '/admin/platform/security-profiles'
    | '/admin/platform/enrichment'
  roles: AdminRole[]
  icon: typeof Users
  featureFlag?: string
}

type AdminSection = {
  title: string
  description: string
  areas: AdminArea[]
}

const adminSections: AdminSection[] = [
  {
    title: 'Tenant operations',
    description: 'Manage tenant structure, operational labeling, and asset-driven automation.',
    areas: [
      {
        title: 'Tenants',
        description: 'Maintain tenant records, review tenant detail, and manage irreversible tenant operations.',
        to: '/admin/tenants',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: Building2,
      },
      {
        title: 'Business labels',
        description: 'Define business labels like Production, Finance, or Customer-facing and reuse them on assets.',
        to: '/admin/business-labels',
        roles: ['GlobalAdmin', 'SecurityManager', 'CustomerAdmin'],
        icon: Tags,
      },
      {
        title: 'Device rules',
        description: 'Automate ownership and security-profile assignment based on tenant device conditions.',
        to: '/admin/device-rules',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: GitBranchPlus,
      },
      {
        title: 'Maintenance',
        description: 'Run tenant-impacting maintenance operations such as resetting remediation state.',
        to: '/admin/maintenance',
        roles: ['GlobalAdmin'],
        icon: Wrench,
      },
    ],
  },
  {
    title: 'Workflows',
    description: 'Configure the logic that shapes triage, ownership, and approval flow.',
    areas: [
      {
        title: 'Workflows',
        description: 'Design and manage triage, assignment, and human approval workflows.',
        to: '/admin/workflows',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: Workflow,
        featureFlag: 'Workflows',
      },
      {
        title: 'Assignment groups',
        description: 'Manage ownership groups used for asset assignment and fallback routing.',
        to: '/admin/teams',
        roles: ['GlobalAdmin', 'SecurityManager', 'SecurityAnalyst', 'AssetOwner', 'TechnicalManager', 'Auditor', 'Stakeholder'],
        icon: ShieldCheck,
      },
      {
        title: 'Sources',
        description: 'Configure tenant data sources and enrichment providers such as Defender and NVD.',
        to: '/admin/sources',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: DatabaseZap,
      },
      {
        title: 'Authenticated scans',
        description: 'Configure on-prem scan runners, tools, connections, and profiles for host scanning.',
        to: '/admin/authenticated-scans',
        roles: ['GlobalAdmin', 'CustomerAdmin'],
        icon: ScanSearch,
      },
    ],
  },
  {
    title: 'Platform configuration',
    description: 'Installation-wide controls for access, integrations, enrichment, and MSSP platform capabilities.',
    areas: [
      {
        title: 'Access control',
        description: 'Review role assignments, tenant access, and who can operate across environments.',
        to: '/admin/platform/access',
        roles: ['GlobalAdmin', 'CustomerAdmin'],
        icon: Users,
      },
      {
        title: 'Security profiles',
        description: 'Create device environment profiles that influence effective vulnerability severity.',
        to: '/admin/platform/security-profiles',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: ShieldEllipsis,
      },
      {
        title: 'Enrichment sources',
        description: 'Configure shared global enrichment providers such as NVD applied across all tenants.',
        to: '/admin/platform/enrichment',
        roles: ['GlobalAdmin'],
        icon: Globe,
      },
      {
        title: 'Integrations',
        description: 'Manage external service connectors such as Microsoft Sentinel.',
        to: '/admin/platform/integrations',
        roles: ['GlobalAdmin'],
        icon: Plug,
      },
      {
        title: 'Advanced tools',
        description: 'Create reusable Defender KQL tools that operators can run from supported asset detail views.',
        to: '/admin/platform/advanced-tools',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: Braces,
      },
      {
        title: 'AI settings',
        description: 'Manage tenant AI profiles, prompts, runtime controls, and default models.',
        to: '/admin/platform/ai',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: Bot,
      },
      {
        title: 'Notification delivery',
        description: 'Configure outbound providers such as SMTP and Mailgun for operational notifications.',
        to: '/admin/platform/notifications',
        roles: ['GlobalAdmin'],
        icon: Bell,
      },
      {
        title: 'Feature flags',
        description: 'Manage per-tenant and per-user feature flag overrides across the platform.',
        to: '/admin/platform/feature-flags',
        roles: ['GlobalAdmin'],
        icon: Flag,
      },
    ],
  },
]

function canAccess(roles: AdminRole[], activeRoles: string[]) {
  return [...activeRoles, 'Stakeholder'].some((role) => roles.includes(role as AdminRole))
}

function AdminLandingPage() {
  const { user } = Route.useRouteContext()
  const activeRoles = user.activeRoles ?? []
  const featureFlags = user.featureFlags ?? {}
  const accessibleSections = adminSections
    .map((section) => ({
      ...section,
      areas: section.areas.filter((area) => {
        if (!canAccess(area.roles, activeRoles)) return false
        if (area.featureFlag && !featureFlags[area.featureFlag]?.isEnabled) return false
        return true
      }),
    }))
    .filter((section) => section.areas.length > 0)

  return (
    <section className="space-y-6">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Admin console
            </p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">
              Admin Console
            </h1>
            <p className="max-w-2xl text-sm text-muted-foreground">
              One control plane for tenant operations, access, workflows,
              integrations, and MSSP-only platform configuration.
            </p>
          </div>
        </div>
      </div>

      <div className="space-y-5">
        {accessibleSections.map((section) => (
          <section key={section.title} className="space-y-3">
            <div className="space-y-1 px-1">
              <h2 className="text-lg font-semibold tracking-tight">{section.title}</h2>
              <p className="text-sm text-muted-foreground">{section.description}</p>
            </div>
            <div className="grid gap-4 lg:grid-cols-2 xl:grid-cols-3">
              {section.areas.map((area) => {
                const Icon = area.icon
                return (
                  <Link key={area.to} to={area.to} className="block">
                    <Card className="h-full rounded-2xl border-border/70 bg-card/92 transition hover:border-primary/30 hover:bg-accent/10">
                      <CardHeader className="space-y-4">
                        <div className="flex items-start justify-between gap-3">
                          <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
                            <Icon className="size-5 text-primary" />
                          </div>
                          <ChevronRight className="size-5 text-muted-foreground" />
                        </div>
                        <div>
                          <CardTitle>{area.title}</CardTitle>
                          <p className="mt-2 text-sm text-muted-foreground">
                            {area.description}
                          </p>
                        </div>
                      </CardHeader>
                      <CardContent>
                        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                          Open section
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
