import {
  Bell,
  Bot,
  Braces,
  Building2,
  Flag,
  GitBranchPlus,
  Globe,
  KeyRound,
  Plug,
  ScanSearch,
  ShieldCheck,
  ShieldEllipsis,
  Tags,
  Users,
  Workflow,
  Wrench,
  type LucideIcon,
} from 'lucide-react'
import type { CurrentUser } from '@/server/auth.functions'

export type AdminRole =
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

export type AdminRouteTo =
  | '/admin'
  | '/admin/teams'
  | '/admin/tenants'
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
  | '/admin/platform/credentials'

export type AdminArea = {
  title: string
  description: string
  to: AdminRouteTo
  roles: AdminRole[]
  icon: LucideIcon
  featureFlag?: string
}

export type AdminSection = {
  title: string
  description: string
  areas: AdminArea[]
}

export const adminSections: AdminSection[] = [
  {
    title: 'Tenant',
    description: 'Tenant structure, labels, ownership, and local operations.',
    areas: [
      {
        title: 'Tenants',
        description: 'Manage tenant identity, sources, AI settings, asset rules, workflows, and business labels.',
        to: '/admin/tenants',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: Building2,
      },
      {
        title: 'Business labels',
        description: 'Define labels such as Production, Finance, or Customer-facing and reuse them on assets.',
        to: '/admin/business-labels',
        roles: ['GlobalAdmin', 'SecurityManager', 'CustomerAdmin'],
        icon: Tags,
      },
      {
        title: 'Asset rules',
        description: 'Automate ownership and security-profile assignment from asset signals.',
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
    title: 'Automation',
    description: 'Workflow, assignment, and scan automation.',
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
        title: 'Authenticated scans',
        description: 'Configure on-prem scan runners, tools, connections, and profiles for host scanning.',
        to: '/admin/authenticated-scans',
        roles: ['GlobalAdmin', 'CustomerAdmin'],
        icon: ScanSearch,
      },
    ],
  },
  {
    title: 'Platform',
    description: 'Installation-wide access, enrichment, integrations, and provider controls.',
    areas: [
      {
        title: 'Access control',
        description: 'Review role assignments, tenant access, and cross-environment operators.',
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
        description: 'Configure shared global enrichment providers such as NVD.',
        to: '/admin/platform/enrichment',
        roles: ['GlobalAdmin'],
        icon: Globe,
      },
      {
        title: 'Stored credentials',
        description: 'Manage reusable credential references for sources and integrations.',
        to: '/admin/platform/credentials',
        roles: ['GlobalAdmin'],
        icon: KeyRound,
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
        description: 'Create reusable Defender KQL tools for supported asset detail views.',
        to: '/admin/platform/advanced-tools',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: Braces,
      },
      {
        title: 'AI settings',
        description: 'Configure platform-level AI settings and tenant profile defaults.',
        to: '/admin/platform/ai',
        roles: ['GlobalAdmin', 'SecurityManager'],
        icon: Bot,
      },
      {
        title: 'Notification delivery',
        description: 'Configure outbound providers such as SMTP and Mailgun.',
        to: '/admin/platform/notifications',
        roles: ['GlobalAdmin'],
        icon: Bell,
      },
      {
        title: 'Feature flags',
        description: 'Manage per-tenant and per-user feature flag overrides.',
        to: '/admin/platform/feature-flags',
        roles: ['GlobalAdmin'],
        icon: Flag,
      },
    ],
  },
]

export function canAccessAdminArea(roles: AdminRole[], activeRoles: string[]) {
  return [...activeRoles, 'Stakeholder'].some((role) => roles.includes(role as AdminRole))
}

export function getAccessibleAdminSections(user: CurrentUser) {
  const activeRoles = user.activeRoles ?? []
  const featureFlags = user.featureFlags ?? {}

  return adminSections
    .map((section) => ({
      ...section,
      areas: section.areas.filter((area) => {
        if (!canAccessAdminArea(area.roles, activeRoles)) return false
        if (area.featureFlag && !featureFlags[area.featureFlag]?.isEnabled) return false
        return true
      }),
    }))
    .filter((section) => section.areas.length > 0)
}
