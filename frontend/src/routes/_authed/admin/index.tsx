import { Link, createFileRoute } from '@tanstack/react-router'
import { Building2, ChevronRight, DatabaseZap, GitBranchPlus, Plug, ShieldCheck, ShieldEllipsis, Tags, Users, Workflow, Wrench } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

export const Route = createFileRoute('/_authed/admin/')({
  component: AdminLandingPage,
})

type AdminArea = {
  title: string
  description: string
  to: '/admin/users' | '/admin/teams' | '/admin/tenants' | '/admin/sources' | '/admin/security-profiles' | '/admin/business-labels' | '/admin/asset-rules' | '/admin/workflows' | '/admin/integrations' | '/admin/maintenance'
  roles: Array<'GlobalAdmin' | 'CustomerAdmin' | 'CustomerOperator' | 'CustomerViewer' | 'SecurityManager' | 'SecurityAnalyst' | 'AssetOwner' | 'TechnicalManager' | 'Auditor' | 'Stakeholder'>
  icon: typeof Users
}

const adminAreas: AdminArea[] = [
  {
    title: 'Users',
    description: 'Review access, role assignments, and who can operate across tenants.',
    to: '/admin/users',
    roles: ['GlobalAdmin', 'CustomerAdmin'],
    icon: Users,
  },
  {
    title: 'Assignment Groups',
    description: 'Create ownership groups and use them for asset assignment and fallback routing.',
    to: '/admin/teams',
    roles: ['GlobalAdmin', 'SecurityManager', 'SecurityAnalyst', 'AssetOwner', 'TechnicalManager', 'Auditor', 'Stakeholder'],
    icon: ShieldCheck,
  },
  {
    title: 'Tenants',
    description: 'Inspect tenant identity and inventory footprint without source-management noise.',
    to: '/admin/tenants',
    roles: ['GlobalAdmin', 'SecurityManager'],
    icon: Building2,
  },
  {
    title: 'Sources',
    description: 'Configure tenant data sources and enrichment providers such as Microsoft Defender and NVD API.',
    to: '/admin/sources',
    roles: ['GlobalAdmin', 'SecurityManager'],
    icon: DatabaseZap,
  },
  {
    title: 'Security Profiles',
    description: 'Create reusable device environment profiles used to recalculate effective vulnerability severity.',
    to: '/admin/security-profiles',
    roles: ['GlobalAdmin', 'SecurityManager'],
    icon: ShieldEllipsis,
  },
  {
    title: 'Business Labels',
    description: 'Define tenant business labels like Production, Finance, or Customer-facing and reuse them on assets.',
    to: '/admin/business-labels',
    roles: ['GlobalAdmin', 'SecurityManager', 'CustomerAdmin'],
    icon: Tags,
  },
  {
    title: 'Asset Rules',
    description: 'Automate security profile and team assignment based on asset filters. Rules run after each ingestion.',
    to: '/admin/asset-rules',
    roles: ['GlobalAdmin', 'SecurityManager'],
    icon: GitBranchPlus,
  },
  {
    title: 'Workflows',
    description: 'Design and manage workflows for vulnerability triage, assignment routing, and human-in-the-loop approvals.',
    to: '/admin/workflows',
    roles: ['GlobalAdmin', 'SecurityManager'],
    icon: Workflow,
  },
  {
    title: 'Integrations',
    description: 'Configure external service connectors such as Microsoft Sentinel for audit log forwarding.',
    to: '/admin/integrations',
    roles: ['GlobalAdmin'],
    icon: Plug,
  },
  {
    title: 'Maintenance',
    description: 'Destructive operations for resetting tenant data such as revoking all remediation decisions.',
    to: '/admin/maintenance',
    roles: ['GlobalAdmin'],
    icon: Wrench,
  },
]

function AdminLandingPage() {
  const { user } = Route.useRouteContext()
  const accessibleAreas = adminAreas.filter((area) =>
    [...(user.activeRoles ?? []), 'Stakeholder'].some((role) => area.roles.includes(role as 'GlobalAdmin' | 'CustomerAdmin' | 'CustomerOperator' | 'CustomerViewer' | 'SecurityManager' | 'SecurityAnalyst' | 'AssetOwner' | 'TechnicalManager' | 'Auditor' | 'Stakeholder')),
  )

  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Administration
            </p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">
              Admin Console
            </h1>
            <p className="max-w-2xl text-sm text-muted-foreground">
              Centralized access to tenant administration, user access control,
              and assignment-group ownership management.
            </p>
          </div>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        {accessibleAreas.map((area) => {
          const Icon = area.icon;
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
                    Open admin area
                  </p>
                </CardContent>
              </Card>
            </Link>
          );
        })}
      </div>
    </section>
  );
}
