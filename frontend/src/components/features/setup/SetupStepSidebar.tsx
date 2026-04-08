import { Info, KeyRound, Network, ShieldCheck } from 'lucide-react'
import { toneBadge } from '@/lib/tone-classes'

type SidebarPanelProps = {
  stepId: string
}

export function SetupStepSidebar({ stepId }: SidebarPanelProps) {
  switch (stepId) {
    case 'entra':
      return <EntraSidebar />
    case 'workspace':
      return <WorkspaceSidebar />
    case 'defender':
      return <DefenderSidebar />
    default:
      return null
  }
}

function EntraSidebar() {
  return (
    <aside className="space-y-5 rounded-2xl border border-border/70 bg-card p-5">
      <div className="flex items-center gap-3">
        <div className="flex size-9 items-center justify-center rounded-lg bg-primary/10">
          <KeyRound className="size-4 text-primary" />
        </div>
        <h3 className="text-base font-semibold text-foreground">Identity Foundation</h3>
      </div>

      <p className="text-sm leading-6 text-muted-foreground">
        The <span className="font-medium text-foreground">Entra application</span> provides the
        identity backbone for PatchHound. Admin consent establishes trust between your Azure AD
        tenant and the platform.
      </p>

      <ul className="space-y-2 text-sm leading-6 text-muted-foreground">
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-primary" />
          Single sign-on for all operators.
        </li>
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-primary" />
          Role-based access inherited from Entra groups.
        </li>
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-primary" />
          Multi-tenant isolation by design.
        </li>
      </ul>

      <div className={`rounded-xl border p-3 text-sm ${toneBadge('info')}`}>
        <span className="font-medium">Coming next</span>
        <p className="mt-1 text-xs leading-5 opacity-80">
          After consent, you'll name your workspace and optionally connect a vulnerability scanner.
        </p>
      </div>
    </aside>
  )
}

function WorkspaceSidebar() {
  return (
    <aside className="space-y-5 rounded-2xl border border-border/70 bg-card p-5">
      <div className="flex items-center gap-3">
        <div className="flex size-9 items-center justify-center rounded-lg bg-primary/10">
          <Network className="size-4 text-primary" />
        </div>
        <h3 className="text-base font-semibold text-foreground">Hub Architecture</h3>
      </div>

      <p className="text-sm leading-6 text-muted-foreground">
        The <span className="font-medium text-foreground">Primary Tenant</span> acts as the root of
        your security hierarchy.
      </p>

      <ul className="space-y-2 text-sm leading-6 text-muted-foreground">
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-foreground" />
          Global policy inheritance across all sub-tenants.
        </li>
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-foreground" />
          Unified billing and subscription management.
        </li>
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-foreground" />
          Single sign-on (SSO) integration point.
        </li>
      </ul>

      <div className={`rounded-xl border p-3 text-sm ${toneBadge('info')}`}>
        <span className="font-medium">Coming next</span>
        <p className="mt-1 text-xs leading-5 opacity-80">
          You'll be able to invite managed customers or sub-departments as independent tenants later.
        </p>
      </div>
    </aside>
  )
}

function DefenderSidebar() {
  return (
    <aside className="space-y-5 rounded-2xl border border-border/70 bg-card p-5">
      <div className="flex items-center gap-3">
        <div className="flex size-9 items-center justify-center rounded-lg bg-primary/10">
          <ShieldCheck className="size-4 text-primary" />
        </div>
        <h3 className="text-base font-semibold text-foreground">Vulnerability Source</h3>
      </div>

      <p className="text-sm leading-6 text-muted-foreground">
        Microsoft Defender for Endpoint provides continuous vulnerability assessments across
        your managed devices.
      </p>

      <ul className="space-y-2 text-sm leading-6 text-muted-foreground">
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-foreground" />
          Real-time CVE detection from agent telemetry.
        </li>
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-foreground" />
          Software inventory synchronized automatically.
        </li>
        <li className="flex items-start gap-2">
          <span className="mt-1.5 size-1.5 shrink-0 rounded-full bg-foreground" />
          Machine-level exposure scoring.
        </li>
      </ul>

      <div className="flex items-start gap-2 rounded-xl border border-border/50 bg-muted/50 p-3 text-sm text-muted-foreground">
        <Info className="mt-0.5 size-3.5 shrink-0 text-muted-foreground" />
        <p className="text-xs leading-5">
          Credentials will be validated via a Defender connectivity test on save.
          You can always configure this later from Sources.
        </p>
      </div>
    </aside>
  )
}
