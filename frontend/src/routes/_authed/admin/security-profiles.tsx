import { useEffect, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { ShieldCheck, Signal, TriangleAlert } from 'lucide-react'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { createSecurityProfile, fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { fetchTenants } from '@/api/settings.functions'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

const environmentClassOptions = ['Workstation', 'Server', 'JumpHost', 'Lab', 'Kiosk', 'OT'] as const
const internetReachabilityOptions = ['Internet', 'InternalNetwork', 'AdjacentOnly', 'LocalOnly'] as const
const requirementOptions = ['Low', 'Medium', 'High'] as const

const fieldGuidance = {
  environmentClass: {
    label: 'Environment Class',
    description: 'Use this to describe the device role. It helps people understand why the profile exists and what kind of endpoint it should be assigned to.',
  },
  internetReachability: {
    label: 'Internet Reachability',
    description: 'This affects exploitability. Devices reachable from the internet should keep more severe network-based exposure than devices limited to internal, adjacent, or local access.',
  },
  confidentialityRequirement: {
    label: 'Confidentiality Requirement',
    description: 'Raise this when data disclosure matters more for this device. Higher values increase the impact of vulnerabilities that expose data.',
  },
  integrityRequirement: {
    label: 'Integrity Requirement',
    description: 'Raise this when unauthorized changes would be especially harmful. Higher values increase the impact of tampering-oriented vulnerabilities.',
  },
  availabilityRequirement: {
    label: 'Availability Requirement',
    description: 'Raise this when uptime matters. Higher values increase the impact of denial-of-service or outage-causing vulnerabilities.',
  },
} as const

const internetReachabilityHelp: Record<(typeof internetReachabilityOptions)[number], string> = {
  Internet: 'Use for externally reachable systems. Network-exploitable vulnerabilities stay highly exposed.',
  InternalNetwork: 'Use for assets only reachable inside your organization. This still allows network exposure, but removes direct internet reachability.',
  AdjacentOnly: 'Use for segmented or same-network access only. This reduces exposure for broader network attack paths.',
  LocalOnly: 'Use for tightly isolated devices that require local presence or an already established foothold.',
}

const requirementHelp: Record<(typeof requirementOptions)[number], string> = {
  Low: 'The business impact is lower for this dimension, so PatchHound reduces how much this factor increases severity.',
  Medium: 'Balanced default. Use when this device does not need special weighting.',
  High: 'The business impact is high for this dimension, so PatchHound increases how much this factor affects severity.',
}

const environmentHelp: Record<(typeof environmentClassOptions)[number], string> = {
  Workstation: 'General user endpoint profile.',
  Server: 'Service-hosting system where confidentiality, integrity, or uptime may matter more.',
  JumpHost: 'Access broker or admin system with elevated exposure and importance.',
  Lab: 'Test or isolated environment where some impact dimensions may be lower.',
  Kiosk: 'Locked-down interactive endpoint with constrained user behavior.',
  OT: 'Operational technology or production control environment where availability often matters more.',
}

export const Route = createFileRoute('/_authed/admin/security-profiles')({
  loader: async () => {
    const [profiles, tenants] = await Promise.all([
      fetchSecurityProfiles({ data: {} }),
      fetchTenants({ data: { page: 1, pageSize: 100 } }),
    ])

    return {
      profiles,
      tenants: tenants.items,
    }
  },
  component: SecurityProfilesPage,
})

function SecurityProfilesPage() {
  const router = useRouter()
  const { user } = Route.useRouteContext()
  const data = Route.useLoaderData()
  const [tenantId, setTenantId] = useState(data.tenants[0]?.id ?? '')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [environmentClass, setEnvironmentClass] = useState<(typeof environmentClassOptions)[number]>(environmentClassOptions[0])
  const [internetReachability, setInternetReachability] = useState<(typeof internetReachabilityOptions)[number]>(internetReachabilityOptions[0])
  const [confidentialityRequirement, setConfidentialityRequirement] = useState<(typeof requirementOptions)[number]>(requirementOptions[1])
  const [integrityRequirement, setIntegrityRequirement] = useState<(typeof requirementOptions)[number]>(requirementOptions[1])
  const [availabilityRequirement, setAvailabilityRequirement] = useState<(typeof requirementOptions)[number]>(requirementOptions[1])
  const tenantNames = new Map(data.tenants.map((tenant) => [tenant.id, tenant.name]))

  const mutation = useMutation({
    mutationFn: async () => {
      await createSecurityProfile({
        data: {
          tenantId,
          name,
          description,
          environmentClass,
          internetReachability,
          confidentialityRequirement,
          integrityRequirement,
          availabilityRequirement,
        },
      })
    },
    onSuccess: async () => {
      setName('')
      setDescription('')
      setEnvironmentClass(environmentClassOptions[0])
      setInternetReachability(internetReachabilityOptions[0])
      setConfidentialityRequirement(requirementOptions[1])
      setIntegrityRequirement(requirementOptions[1])
      setAvailabilityRequirement(requirementOptions[1])
      await router.invalidate()
    },
  })
  const canViewAudit = user.roles.includes('GlobalAdmin') || user.roles.includes('Auditor')
  const recentAuditMutation = useMutation({
    mutationFn: async () =>
      fetchAuditLog({
        data: {
          entityType: 'AssetSecurityProfile',
          page: 1,
          pageSize: 5,
        },
      }),
  })

  useEffect(() => {
    if (canViewAudit && !recentAuditMutation.data && !recentAuditMutation.isPending) {
      void recentAuditMutation.mutateAsync()
    }
  }, [canViewAudit, recentAuditMutation.data, recentAuditMutation.isPending, recentAuditMutation.mutateAsync])

  return (
    <section className="space-y-4 pb-4">
      <Card className="rounded-[30px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_52%),var(--color-card)]">
        <CardHeader className="space-y-3">
          <Badge variant="outline" className="w-fit rounded-full border-primary/20 bg-primary/10 text-primary">
            Environmental Severity
          </Badge>
          <CardTitle className="text-3xl font-semibold tracking-[-0.04em]">
            Security profiles explain why a device should score differently than vendor baseline severity.
          </CardTitle>
          <p className="max-w-4xl text-sm leading-6 text-muted-foreground">
            These profiles do not change the CVE itself. They describe the device environment so PatchHound can
            recalculate effective severity using reachability and business impact requirements.
          </p>
        </CardHeader>
        <CardContent className="grid gap-4 lg:grid-cols-3">
          <GuideCard
            icon={Signal}
            title="Reachability changes exploitability"
            text="A device reachable from the internet should keep more severe network exposure than a device that is only local or tightly segmented."
          />
          <GuideCard
            icon={ShieldCheck}
            title="Requirements change impact"
            text="Confidentiality, integrity, and availability tell PatchHound which impact dimensions should weigh more heavily for this environment."
          />
          <GuideCard
            icon={TriangleAlert}
            title="Use profiles deliberately"
            text="Choose a small set of reusable profiles. If every device gets a one-off profile, the scoring model becomes hard to trust."
          />
        </CardContent>
      </Card>

      <Card className="rounded-[30px] border-border/70 bg-card/82">
        <CardHeader>
          <CardTitle>Create Security Profile</CardTitle>
          <p className="text-sm text-muted-foreground">
            Define the device environment once, then assign it to assets that should share the same severity logic.
          </p>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 md:grid-cols-2">
            <FieldBlock
              label="Tenant"
              description="Choose which tenant can assign and use this profile."
              control={(
                <select
                  className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
                  value={tenantId}
                  onChange={(event) => setTenantId(event.target.value)}
                >
                  {data.tenants.map((tenant) => (
                    <option key={tenant.id} value={tenant.id}>
                      {tenant.name}
                    </option>
                  ))}
                </select>
              )}
            />
            <FieldBlock
              label="Profile Name"
              description="Use a clear operational name such as “Internet-facing server” or “Internal workstation”."
              control={(
                <Input
                  placeholder="Internet-facing server"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />
              )}
            />
          </div>

          <FieldBlock
            label="Description"
            description="Write the assignment intent so admins know when this profile should be used."
            control={(
              <Input
                placeholder="Use for production servers reachable from the public internet."
                value={description}
                onChange={(event) => setDescription(event.target.value)}
              />
            )}
          />

          <div className="grid gap-4 xl:grid-cols-2">
            <FieldBlock
              label={fieldGuidance.environmentClass.label}
              description={fieldGuidance.environmentClass.description}
              helper={environmentHelp[environmentClass]}
              control={(
                <select
                  className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
                  value={environmentClass}
                  onChange={(event) => setEnvironmentClass(event.target.value as (typeof environmentClassOptions)[number])}
                >
                  {environmentClassOptions.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              )}
            />
            <FieldBlock
              label={fieldGuidance.internetReachability.label}
              description={fieldGuidance.internetReachability.description}
              helper={internetReachabilityHelp[internetReachability]}
              control={(
                <select
                  className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
                  value={internetReachability}
                  onChange={(event) => setInternetReachability(event.target.value as (typeof internetReachabilityOptions)[number])}
                >
                  {internetReachabilityOptions.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              )}
            />
          </div>

          <div className="grid gap-4 xl:grid-cols-3">
            <FieldBlock
              label={fieldGuidance.confidentialityRequirement.label}
              description={fieldGuidance.confidentialityRequirement.description}
              helper={requirementHelp[confidentialityRequirement]}
              control={(
                <select
                  className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
                  value={confidentialityRequirement}
                  onChange={(event) => setConfidentialityRequirement(event.target.value as (typeof requirementOptions)[number])}
                >
                  {requirementOptions.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              )}
            />
            <FieldBlock
              label={fieldGuidance.integrityRequirement.label}
              description={fieldGuidance.integrityRequirement.description}
              helper={requirementHelp[integrityRequirement]}
              control={(
                <select
                  className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
                  value={integrityRequirement}
                  onChange={(event) => setIntegrityRequirement(event.target.value as (typeof requirementOptions)[number])}
                >
                  {requirementOptions.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              )}
            />
            <FieldBlock
              label={fieldGuidance.availabilityRequirement.label}
              description={fieldGuidance.availabilityRequirement.description}
              helper={requirementHelp[availabilityRequirement]}
              control={(
                <select
                  className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
                  value={availabilityRequirement}
                  onChange={(event) => setAvailabilityRequirement(event.target.value as (typeof requirementOptions)[number])}
                >
                  {requirementOptions.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              )}
            />
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Button
              disabled={mutation.isPending || !tenantId.trim() || !name.trim()}
              onClick={() => {
                mutation.mutate()
              }}
            >
              {mutation.isPending ? 'Creating profile...' : 'Create profile'}
            </Button>
            <p className="text-sm text-muted-foreground">
              Profiles can be assigned later from the asset inspector or the asset detail page.
            </p>
          </div>
        </CardContent>
      </Card>

      <Card className="rounded-[30px] border-border/70 bg-card/82">
        <CardHeader>
          <div className="flex items-end justify-between gap-3">
            <div>
              <CardTitle>Profiles</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Review the current severity logic each tenant can apply to devices.
              </p>
            </div>
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{data.profiles.totalCount} total</p>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          {data.profiles.items.length === 0 ? (
            <div className="rounded-2xl border border-border/60 bg-background/30 px-4 py-6 text-sm text-muted-foreground">
              No security profiles found.
            </div>
          ) : (
            data.profiles.items.map((profile) => (
              <div key={profile.id} className="rounded-[24px] border border-border/70 bg-background/30 p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="text-lg font-semibold">{profile.name}</p>
                    <p className="mt-1 text-sm text-muted-foreground">{profile.description ?? 'No description provided.'}</p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="outline" className="rounded-full border-border/70 bg-background/60 text-foreground">
                      {tenantNames.get(profile.tenantId) ?? profile.tenantId}
                    </Badge>
                    <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 text-primary">
                      {profile.environmentClass}
                    </Badge>
                  </div>
                </div>

                <div className="mt-4 grid gap-3 xl:grid-cols-4">
                  <ProfileMetric
                    label="Reachability"
                    value={profile.internetReachability}
                    explanation={internetReachabilityHelp[profile.internetReachability as (typeof internetReachabilityOptions)[number]]}
                  />
                  <ProfileMetric
                    label="Confidentiality"
                    value={profile.confidentialityRequirement}
                    explanation={requirementHelp[profile.confidentialityRequirement as (typeof requirementOptions)[number]]}
                  />
                  <ProfileMetric
                    label="Integrity"
                    value={profile.integrityRequirement}
                    explanation={requirementHelp[profile.integrityRequirement as (typeof requirementOptions)[number]]}
                  />
                  <ProfileMetric
                    label="Availability"
                    value={profile.availabilityRequirement}
                    explanation={requirementHelp[profile.availabilityRequirement as (typeof requirementOptions)[number]]}
                  />
                </div>

                <p className="mt-4 text-xs text-muted-foreground">
                  Updated {new Date(profile.updatedAt).toLocaleString()}
                </p>
              </div>
            ))
          )}
        </CardContent>
      </Card>

      {canViewAudit ? (
        <RecentAuditPanel
          title="Profile Activity"
          description="Recent profile changes are shown here so security teams can see when severity logic changed."
          items={recentAuditMutation.data?.items ?? []}
          emptyMessage="No recent security profile changes have been recorded."
        />
      ) : null}
    </section>
  )
}

function FieldBlock({
  label,
  description,
  helper,
  control,
}: {
  label: string
  description: string
  helper?: string
  control: React.ReactNode
}) {
  return (
    <div className="space-y-2 rounded-[22px] border border-border/70 bg-background/25 p-4">
      <div className="space-y-1">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
      {control}
      {helper ? <p className="text-xs leading-5 text-primary">{helper}</p> : null}
    </div>
  )
}

function GuideCard({
  icon: Icon,
  title,
  text,
}: {
  icon: typeof Signal
  title: string
  text: string
}) {
  return (
    <div className="rounded-[24px] border border-border/70 bg-background/28 p-4">
      <div className="flex items-center gap-3">
        <span className="flex size-10 items-center justify-center rounded-2xl border border-primary/20 bg-primary/10 text-primary">
          <Icon className="size-4" />
        </span>
        <p className="font-medium">{title}</p>
      </div>
      <p className="mt-3 text-sm leading-6 text-muted-foreground">{text}</p>
    </div>
  )
}

function ProfileMetric({
  label,
  value,
  explanation,
}: {
  label: string
  value: string
  explanation: string
}) {
  return (
    <div className="rounded-2xl border border-border/60 bg-card/50 p-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <Badge variant="outline" className="rounded-full border-border/70 bg-background/60 text-foreground">
          {value}
        </Badge>
      </div>
      <p className="mt-3 text-xs leading-5 text-muted-foreground">{explanation}</p>
    </div>
  )
}
