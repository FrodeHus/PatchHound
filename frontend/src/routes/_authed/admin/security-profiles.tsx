import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { ShieldCheck, Signal, TriangleAlert } from 'lucide-react'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { createSecurityProfile, fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { PaginationControls } from '@/components/ui/pagination-controls'
import {
  securityProfileEnvironmentClassOptions,
  securityProfileEnvironmentHelp,
  securityProfileFieldGuidance,
  securityProfileInternetReachabilityHelp,
  securityProfileInternetReachabilityOptions,
  securityProfileRequirementHelp,
  securityProfileRequirementOptions,
} from '@/lib/options/security-profiles'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/security-profiles')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    return fetchSecurityProfiles({
      data: {
        page: deps.page,
        pageSize: deps.pageSize,
      },
    })
  },
  component: SecurityProfilesPage,
})

function SecurityProfilesPage() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { user } = Route.useRouteContext()
  const initialProfiles = Route.useLoaderData()
  const queryClient = useQueryClient()
  const { selectedTenantId, tenants } = useTenantScope()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [environmentClass, setEnvironmentClass] = useState<(typeof securityProfileEnvironmentClassOptions)[number]>(securityProfileEnvironmentClassOptions[0])
  const [internetReachability, setInternetReachability] = useState<(typeof securityProfileInternetReachabilityOptions)[number]>(securityProfileInternetReachabilityOptions[0])
  const [confidentialityRequirement, setConfidentialityRequirement] = useState<(typeof securityProfileRequirementOptions)[number]>(securityProfileRequirementOptions[1])
  const [integrityRequirement, setIntegrityRequirement] = useState<(typeof securityProfileRequirementOptions)[number]>(securityProfileRequirementOptions[1])
  const [availabilityRequirement, setAvailabilityRequirement] = useState<(typeof securityProfileRequirementOptions)[number]>(securityProfileRequirementOptions[1])
  const tenantNames = new Map(tenants.map((tenant) => [tenant.id, tenant.name]))
  const profilesQuery = useQuery({
    queryKey: ['security-profiles', selectedTenantId, search.page, search.pageSize],
    queryFn: () => fetchSecurityProfiles({ data: { page: search.page, pageSize: search.pageSize } }),
    initialData: initialProfiles,
    staleTime: 30_000,
  })

  const mutation = useMutation({
    mutationFn: async () => {
      await createSecurityProfile({
        data: {
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
      setEnvironmentClass(securityProfileEnvironmentClassOptions[0])
      setInternetReachability(securityProfileInternetReachabilityOptions[0])
      setConfidentialityRequirement(securityProfileRequirementOptions[1])
      setIntegrityRequirement(securityProfileRequirementOptions[1])
      setAvailabilityRequirement(securityProfileRequirementOptions[1])
      await queryClient.invalidateQueries({ queryKey: ['security-profiles'] })
      if (canViewAudit) {
        await queryClient.invalidateQueries({ queryKey: ['audit-log', 'AssetSecurityProfile', selectedTenantId] })
      }
    },
  })
  const canViewAudit = user.roles.includes('GlobalAdmin') || user.roles.includes('Auditor')
  const recentAuditQuery = useQuery({
    queryKey: ['audit-log', 'AssetSecurityProfile', selectedTenantId],
    queryFn: async () =>
      fetchAuditLog({
        data: {
          entityType: 'AssetSecurityProfile',
          page: 1,
          pageSize: 5,
        },
      }),
    enabled: canViewAudit,
    staleTime: 30_000,
  })
  const recentAuditItems = recentAuditQuery.data?.items ?? []

  const profilePage = profilesQuery.data

  return (
    <section className="space-y-4 pb-4">
      <Card className="rounded-2xl border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_52%),var(--color-card)]">
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

      <Card className="rounded-2xl">
        <CardHeader>
          <CardTitle>Create Security Profile</CardTitle>
          <p className="text-sm text-muted-foreground">
            Define the device environment once for the tenant selected in the top bar, then assign it to assets that should share the same severity logic.
          </p>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 md:grid-cols-2">
            <FieldBlock
              label="Active Tenant"
              description="This form follows the tenant scope in the top navigation."
              control={<div className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm font-medium">{tenantNames.get(selectedTenantId ?? '') ?? 'No tenant selected'}</div>}
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
              label={securityProfileFieldGuidance.environmentClass.label}
              description={securityProfileFieldGuidance.environmentClass.description}
              helper={securityProfileEnvironmentHelp[environmentClass]}
              control={(
                <Select
                  value={environmentClass}
                  onValueChange={(value) => {
                    if (value) {
                      setEnvironmentClass(value)
                    }
                  }}
                >
                  <SelectTrigger className="h-10 w-full rounded-xl bg-background px-3">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                    {securityProfileEnvironmentClassOptions.map((option) => (
                      <SelectItem key={option} value={option}>
                        {option}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            <FieldBlock
              label={securityProfileFieldGuidance.internetReachability.label}
              description={securityProfileFieldGuidance.internetReachability.description}
              helper={securityProfileInternetReachabilityHelp[internetReachability]}
              control={(
                <Select
                  value={internetReachability}
                  onValueChange={(value) => {
                    if (value) {
                      setInternetReachability(value)
                    }
                  }}
                >
                  <SelectTrigger className="h-10 w-full rounded-xl bg-background px-3">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                    {securityProfileInternetReachabilityOptions.map((option) => (
                      <SelectItem key={option} value={option}>
                        {option}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>

          <div className="grid gap-4 xl:grid-cols-3">
            <FieldBlock
              label={securityProfileFieldGuidance.confidentialityRequirement.label}
              description={securityProfileFieldGuidance.confidentialityRequirement.description}
              helper={securityProfileRequirementHelp[confidentialityRequirement]}
              control={(
                <Select
                  value={confidentialityRequirement}
                  onValueChange={(value) => {
                    if (value) {
                      setConfidentialityRequirement(value)
                    }
                  }}
                >
                  <SelectTrigger className="h-10 w-full rounded-xl bg-background px-3">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                    {securityProfileRequirementOptions.map((option) => (
                      <SelectItem key={option} value={option}>
                        {option}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            <FieldBlock
              label={securityProfileFieldGuidance.integrityRequirement.label}
              description={securityProfileFieldGuidance.integrityRequirement.description}
              helper={securityProfileRequirementHelp[integrityRequirement]}
              control={(
                <Select
                  value={integrityRequirement}
                  onValueChange={(value) => {
                    if (value) {
                      setIntegrityRequirement(value)
                    }
                  }}
                >
                  <SelectTrigger className="h-10 w-full rounded-xl bg-background px-3">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                    {securityProfileRequirementOptions.map((option) => (
                      <SelectItem key={option} value={option}>
                        {option}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            <FieldBlock
              label={securityProfileFieldGuidance.availabilityRequirement.label}
              description={securityProfileFieldGuidance.availabilityRequirement.description}
              helper={securityProfileRequirementHelp[availabilityRequirement]}
              control={(
                <Select
                  value={availabilityRequirement}
                  onValueChange={(value) => {
                    if (value) {
                      setAvailabilityRequirement(value)
                    }
                  }}
                >
                  <SelectTrigger className="h-10 w-full rounded-xl bg-background px-3">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                    {securityProfileRequirementOptions.map((option) => (
                      <SelectItem key={option} value={option}>
                        {option}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Button
              disabled={mutation.isPending || !selectedTenantId?.trim() || !name.trim()}
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

      <Card className="rounded-2xl border-border/70 bg-card/82">
        <CardHeader>
          <div className="flex items-end justify-between gap-3">
            <div>
              <CardTitle>Profiles</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Review the current severity logic for the tenant selected in the top bar.
              </p>
            </div>
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{profilePage?.totalCount ?? 0} total</p>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          {!selectedTenantId ? (
            <InsetPanel className="px-4 py-6 text-sm text-muted-foreground">
              Choose a tenant from the top bar to review security profiles.
            </InsetPanel>
          ) : profilesQuery.isPending && !profilePage ? (
            <InsetPanel className="px-4 py-6 text-sm text-muted-foreground">
              Loading security profiles...
            </InsetPanel>
          ) : profilePage && profilePage.items.length === 0 ? (
            <InsetPanel className="px-4 py-6 text-sm text-muted-foreground">
              No security profiles found.
            </InsetPanel>
          ) : (
            profilePage?.items.map((profile) => (
              <InsetPanel key={profile.id} className="rounded-xl p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="text-lg font-semibold">{profile.name}</p>
                    <p className="mt-1 text-sm text-muted-foreground">{profile.description ?? 'No description provided.'}</p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="outline" className="rounded-full border-border/80 bg-card text-foreground">
                      {tenantNames.get(profile.tenantId) ?? 'Unknown tenant'}
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
                    explanation={
                      securityProfileInternetReachabilityHelp[
                        profile.internetReachability as (typeof securityProfileInternetReachabilityOptions)[number]
                      ]
                    }
                  />
                  <ProfileMetric
                    label="Confidentiality"
                    value={profile.confidentialityRequirement}
                    explanation={
                      securityProfileRequirementHelp[
                        profile.confidentialityRequirement as (typeof securityProfileRequirementOptions)[number]
                      ]
                    }
                  />
                  <ProfileMetric
                    label="Integrity"
                    value={profile.integrityRequirement}
                    explanation={
                      securityProfileRequirementHelp[
                        profile.integrityRequirement as (typeof securityProfileRequirementOptions)[number]
                      ]
                    }
                  />
                  <ProfileMetric
                    label="Availability"
                    value={profile.availabilityRequirement}
                    explanation={
                      securityProfileRequirementHelp[
                        profile.availabilityRequirement as (typeof securityProfileRequirementOptions)[number]
                      ]
                    }
                  />
                </div>

                <p className="mt-4 text-xs text-muted-foreground">
                  Updated {new Date(profile.updatedAt).toLocaleString()}
                </p>
              </InsetPanel>
            ))
          )}
          {profilePage ? (
            <PaginationControls
              page={profilePage.page}
              pageSize={profilePage.pageSize}
              totalCount={profilePage.totalCount}
              totalPages={profilePage.totalPages}
              onPageChange={(page) => {
                void navigate({
                  search: (prev) => ({ ...prev, page }),
                })
              }}
              onPageSizeChange={(nextPageSize) => {
                void navigate({
                  search: (prev) => ({ ...prev, pageSize: nextPageSize, page: 1 }),
                })
              }}
            />
          ) : null}
        </CardContent>
      </Card>

      {canViewAudit ? (
        <RecentAuditPanel
          title="Profile Activity"
          description="Recent profile changes are shown here so security teams can see when severity logic changed."
          items={recentAuditItems}
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
    <InsetPanel className="space-y-2 rounded-[22px] p-4">
      <div className="space-y-1">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
      {control}
      {helper ? <p className="text-xs leading-5 text-primary">{helper}</p> : null}
    </InsetPanel>
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
    <InsetPanel emphasis="subtle" className="rounded-xl p-4">
      <div className="flex items-center gap-3">
        <span className="flex size-10 items-center justify-center rounded-2xl border border-primary/20 bg-primary/10 text-primary">
          <Icon className="size-4" />
        </span>
        <p className="font-medium">{title}</p>
      </div>
      <p className="mt-3 text-sm leading-6 text-muted-foreground">{text}</p>
    </InsetPanel>
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
    <InsetPanel emphasis="strong" className="p-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        <Badge variant="outline" className="rounded-full border-border/80 bg-muted/75 text-foreground">
          {value}
        </Badge>
      </div>
      <p className="mt-3 text-xs leading-5 text-muted-foreground">{explanation}</p>
    </InsetPanel>
  )
}
