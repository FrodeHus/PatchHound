import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { CircleHelp, PenSquare, Plus, ShieldCheck, Signal, TriangleAlert } from 'lucide-react'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { createSecurityProfile, fetchSecurityProfiles, updateSecurityProfile } from '@/api/security-profiles.functions'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { CvssWorkbenchTrigger } from '@/components/features/vulnerabilities/CvssCalculator'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Separator } from '@/components/ui/separator'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import {
  securityProfileModifiedAttackComplexityHelp,
  securityProfileModifiedAttackComplexityOptions,
  securityProfileModifiedAttackVectorHelp,
  securityProfileModifiedAttackVectorOptions,
  securityProfileModifiedImpactHelp,
  securityProfileModifiedImpactOptions,
  securityProfileModifiedPrivilegesRequiredHelp,
  securityProfileModifiedPrivilegesRequiredOptions,
  securityProfileModifiedScopeHelp,
  securityProfileModifiedScopeOptions,
  securityProfileModifiedUserInteractionHelp,
  securityProfileModifiedUserInteractionOptions,
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

type SecurityProfileDraft = {
  name: string
  description: string
  environmentClass: (typeof securityProfileEnvironmentClassOptions)[number]
  internetReachability: (typeof securityProfileInternetReachabilityOptions)[number]
  confidentialityRequirement: (typeof securityProfileRequirementOptions)[number]
  integrityRequirement: (typeof securityProfileRequirementOptions)[number]
  availabilityRequirement: (typeof securityProfileRequirementOptions)[number]
  modifiedAttackVector: (typeof securityProfileModifiedAttackVectorOptions)[number]
  modifiedAttackComplexity: (typeof securityProfileModifiedAttackComplexityOptions)[number]
  modifiedPrivilegesRequired: (typeof securityProfileModifiedPrivilegesRequiredOptions)[number]
  modifiedUserInteraction: (typeof securityProfileModifiedUserInteractionOptions)[number]
  modifiedScope: (typeof securityProfileModifiedScopeOptions)[number]
  modifiedConfidentialityImpact: (typeof securityProfileModifiedImpactOptions)[number]
  modifiedIntegrityImpact: (typeof securityProfileModifiedImpactOptions)[number]
  modifiedAvailabilityImpact: (typeof securityProfileModifiedImpactOptions)[number]
}

const defaultDraft = (): SecurityProfileDraft => ({
  name: '',
  description: '',
  environmentClass: securityProfileEnvironmentClassOptions[0],
  internetReachability: securityProfileInternetReachabilityOptions[0],
  confidentialityRequirement: securityProfileRequirementOptions[1],
  integrityRequirement: securityProfileRequirementOptions[1],
  availabilityRequirement: securityProfileRequirementOptions[1],
  modifiedAttackVector: securityProfileModifiedAttackVectorOptions[0],
  modifiedAttackComplexity: securityProfileModifiedAttackComplexityOptions[0],
  modifiedPrivilegesRequired: securityProfileModifiedPrivilegesRequiredOptions[0],
  modifiedUserInteraction: securityProfileModifiedUserInteractionOptions[0],
  modifiedScope: securityProfileModifiedScopeOptions[0],
  modifiedConfidentialityImpact: securityProfileModifiedImpactOptions[0],
  modifiedIntegrityImpact: securityProfileModifiedImpactOptions[0],
  modifiedAvailabilityImpact: securityProfileModifiedImpactOptions[0],
})

function toDraft(profile: SecurityProfile): SecurityProfileDraft {
  return {
    name: profile.name,
    description: profile.description ?? '',
    environmentClass: profile.environmentClass as (typeof securityProfileEnvironmentClassOptions)[number],
    internetReachability: profile.internetReachability as (typeof securityProfileInternetReachabilityOptions)[number],
    confidentialityRequirement: profile.confidentialityRequirement as (typeof securityProfileRequirementOptions)[number],
    integrityRequirement: profile.integrityRequirement as (typeof securityProfileRequirementOptions)[number],
    availabilityRequirement: profile.availabilityRequirement as (typeof securityProfileRequirementOptions)[number],
    modifiedAttackVector: profile.modifiedAttackVector as (typeof securityProfileModifiedAttackVectorOptions)[number],
    modifiedAttackComplexity:
      profile.modifiedAttackComplexity as (typeof securityProfileModifiedAttackComplexityOptions)[number],
    modifiedPrivilegesRequired:
      profile.modifiedPrivilegesRequired as (typeof securityProfileModifiedPrivilegesRequiredOptions)[number],
    modifiedUserInteraction:
      profile.modifiedUserInteraction as (typeof securityProfileModifiedUserInteractionOptions)[number],
    modifiedScope: profile.modifiedScope as (typeof securityProfileModifiedScopeOptions)[number],
    modifiedConfidentialityImpact:
      profile.modifiedConfidentialityImpact as (typeof securityProfileModifiedImpactOptions)[number],
    modifiedIntegrityImpact: profile.modifiedIntegrityImpact as (typeof securityProfileModifiedImpactOptions)[number],
    modifiedAvailabilityImpact:
      profile.modifiedAvailabilityImpact as (typeof securityProfileModifiedImpactOptions)[number],
  }
}

function SecurityProfilesPage() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { user } = Route.useRouteContext()
  const initialProfiles = Route.useLoaderData()
  const queryClient = useQueryClient()
  const { selectedTenantId, tenants } = useTenantScope()
  const [sheetOpen, setSheetOpen] = useState(false)
  const [editingProfile, setEditingProfile] = useState<SecurityProfile | null>(null)
  const [draft, setDraft] = useState<SecurityProfileDraft>(defaultDraft)
  const tenantNames = new Map(tenants.map((tenant) => [tenant.id, tenant.name]))
  const canViewAudit = user.roles.includes('GlobalAdmin') || user.roles.includes('Auditor')

  const profilesQuery = useQuery({
    queryKey: ['security-profiles', selectedTenantId, search.page, search.pageSize],
    queryFn: () => fetchSecurityProfiles({ data: { page: search.page, pageSize: search.pageSize } }),
    initialData: initialProfiles,
    staleTime: 30_000,
  })

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

  const saveMutation = useMutation({
    mutationFn: async () => {
      const payload = {
        name: draft.name,
        description: draft.description,
        environmentClass: draft.environmentClass,
        internetReachability: draft.internetReachability,
        confidentialityRequirement: draft.confidentialityRequirement,
        integrityRequirement: draft.integrityRequirement,
        availabilityRequirement: draft.availabilityRequirement,
        modifiedAttackVector: draft.modifiedAttackVector,
        modifiedAttackComplexity: draft.modifiedAttackComplexity,
        modifiedPrivilegesRequired: draft.modifiedPrivilegesRequired,
        modifiedUserInteraction: draft.modifiedUserInteraction,
        modifiedScope: draft.modifiedScope,
        modifiedConfidentialityImpact: draft.modifiedConfidentialityImpact,
        modifiedIntegrityImpact: draft.modifiedIntegrityImpact,
        modifiedAvailabilityImpact: draft.modifiedAvailabilityImpact,
      }

      if (editingProfile) {
        await updateSecurityProfile({
          data: {
            id: editingProfile.id,
            ...payload,
          },
        })
        return
      }

      await createSecurityProfile({ data: payload })
    },
    onSuccess: async () => {
      setSheetOpen(false)
      setEditingProfile(null)
      setDraft(defaultDraft())
      await queryClient.invalidateQueries({ queryKey: ['security-profiles'] })
      if (canViewAudit) {
        await queryClient.invalidateQueries({ queryKey: ['audit-log', 'AssetSecurityProfile', selectedTenantId] })
      }
    },
  })

  const profilePage = profilesQuery.data
  const recentAuditItems = recentAuditQuery.data?.items ?? []
  const canSave = !!selectedTenantId?.trim() && draft.name.trim().length > 0
  const sheetTitle = editingProfile ? 'Edit security profile' : 'Create security profile'
  const sheetDescription = editingProfile
    ? 'Update the severity context for this reusable tenant profile.'
    : 'Define a reusable environment profile for the tenant selected in the top bar.'
  const selectedTenantName = tenantNames.get(selectedTenantId ?? '') ?? 'No tenant selected'

  return (
    <TooltipProvider>
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

        <Card className="rounded-2xl border-border/70 bg-card/82">
          <CardHeader>
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <CardTitle>Profiles</CardTitle>
                <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                  Review the current severity logic for the tenant selected in the top bar, then create or edit
                  profiles in the side panel.
                </p>
              </div>
              <div className="flex items-center gap-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  {profilePage?.totalCount ?? 0} total
                </p>
                <Button
                  type="button"
                  disabled={!selectedTenantId}
                  onClick={() => {
                    setEditingProfile(null)
                    setDraft(defaultDraft())
                    setSheetOpen(true)
                  }}
                >
                  <Plus className="size-4" />
                  New profile
                </Button>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {!selectedTenantId ? (
              <InsetPanel className="flex items-center justify-between gap-4 px-4 py-6 text-sm text-muted-foreground">
                <span>Choose a tenant from the top bar to review and edit security profiles.</span>
                <Button type="button" disabled variant="outline">
                  <Plus className="size-4" />
                  New profile
                </Button>
              </InsetPanel>
            ) : profilesQuery.isPending && !profilePage ? (
              <InsetPanel className="px-4 py-6 text-sm text-muted-foreground">
                Loading security profiles...
              </InsetPanel>
            ) : profilePage && profilePage.items.length === 0 ? (
              <InsetPanel className="flex flex-wrap items-center justify-between gap-4 px-4 py-6">
                <div className="space-y-1">
                  <p className="font-medium text-foreground">No security profiles yet.</p>
                  <p className="text-sm text-muted-foreground">
                    Create the first reusable severity profile for {selectedTenantName}.
                  </p>
                </div>
                <Button
                  type="button"
                  onClick={() => {
                    setEditingProfile(null)
                    setDraft(defaultDraft())
                    setSheetOpen(true)
                  }}
                >
                  <Plus className="size-4" />
                  Create profile
                </Button>
              </InsetPanel>
            ) : (
              profilePage?.items.map((profile) => (
                <InsetPanel key={profile.id} className="rounded-xl p-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="text-lg font-semibold">{profile.name}</p>
                        <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 text-primary">
                          {profile.environmentClass}
                        </Badge>
                      </div>
                      <p className="mt-1 text-sm text-muted-foreground">
                        {profile.description ?? 'No description provided.'}
                      </p>
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant="outline" className="rounded-full border-border/80 bg-card text-foreground">
                        {tenantNames.get(profile.tenantId) ?? 'Unknown tenant'}
                      </Badge>
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() => {
                          setEditingProfile(profile)
                          setDraft(toDraft(profile))
                          setSheetOpen(true)
                        }}
                      >
                        <PenSquare className="size-4" />
                        Edit
                      </Button>
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
                    <ProfileMetric
                      label="Modified AV"
                      value={profile.modifiedAttackVector}
                      explanation={
                        securityProfileModifiedAttackVectorHelp[
                          profile.modifiedAttackVector as (typeof securityProfileModifiedAttackVectorOptions)[number]
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

        <SecurityProfileSheet
          open={sheetOpen}
          title={sheetTitle}
          description={sheetDescription}
          selectedTenantName={selectedTenantName}
          editingProfile={editingProfile}
          draft={draft}
          isSaving={saveMutation.isPending}
          canSave={canSave}
          onOpenChange={(open) => {
            setSheetOpen(open)
            if (!open) {
              setEditingProfile(null)
              setDraft(defaultDraft())
            }
          }}
          onDraftChange={setDraft}
          onSave={() => saveMutation.mutate()}
        />
      </section>
    </TooltipProvider>
  )
}

function SecurityProfileSheet({
  open,
  title,
  description,
  selectedTenantName,
  editingProfile,
  draft,
  isSaving,
  canSave,
  onOpenChange,
  onDraftChange,
  onSave,
}: {
  open: boolean
  title: string
  description: string
  selectedTenantName: string
  editingProfile: SecurityProfile | null
  draft: SecurityProfileDraft
  isSaving: boolean
  canSave: boolean
  onOpenChange: (open: boolean) => void
  onDraftChange: React.Dispatch<React.SetStateAction<SecurityProfileDraft>>
  onSave: () => void
}) {
  const saveLabel = editingProfile ? 'Save changes' : 'Create profile'

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full sm:max-w-2xl">
        <SheetHeader className="border-b border-border/60">
          <SheetTitle>{title}</SheetTitle>
          <SheetDescription>{description}</SheetDescription>
        </SheetHeader>

        <div className="flex-1 space-y-6 overflow-y-auto p-5">
          <FormSection
            title="Identity"
            description="Name the profile and confirm the tenant scope before defining how severity should be adjusted."
          >
            <div className="grid gap-5 md:grid-cols-2">
              <FieldBlock
                label="Active Tenant"
                description="This form follows the tenant scope in the top navigation."
                control={<div className="rounded-lg border border-border/75 bg-muted/55 px-3 py-3 text-sm font-medium">{selectedTenantName}</div>}
              />
              <FieldBlock
                label="Profile Name"
                description="Use a clear operational name such as “Internet-facing server” or “Internal workstation”."
                control={(
                  <Input
                    placeholder="Internet-facing server"
                    value={draft.name}
                    onChange={(event) => onDraftChange((current) => ({ ...current, name: event.target.value }))}
                    className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]"
                  />
                )}
              />
            </div>
          </FormSection>

          <FormSection
            title="Exposure Context"
            description="Describe the environment this profile represents so PatchHound can weight exploitability correctly."
          >
            <div className="grid gap-5">
              <FieldBlock
                label="Description"
                description="Write the assignment intent so admins know when this profile should be used."
                control={(
                  <Input
                    placeholder="Use for production servers reachable from the public internet."
                    value={draft.description}
                    onChange={(event) => onDraftChange((current) => ({ ...current, description: event.target.value }))}
                    className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]"
                  />
                )}
              />

              <div className="grid gap-5 xl:grid-cols-2">
                <FieldBlock
                  label={securityProfileFieldGuidance.environmentClass.label}
                  description={securityProfileFieldGuidance.environmentClass.description}
                  helper={securityProfileEnvironmentHelp[draft.environmentClass]}
                  control={(
                    <Select
                      value={draft.environmentClass}
                      onValueChange={(value) => {
                        if (value) {
                          onDraftChange((current) => ({
                            ...current,
                            environmentClass: value,
                          }))
                        }
                      }}
                    >
                      <SelectTrigger className="h-11 w-full rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] px-3 shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent className="rounded-xl border-border/80 bg-popover/98 backdrop-blur">
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
                  helper={securityProfileInternetReachabilityHelp[draft.internetReachability]}
                  control={(
                    <Select
                      value={draft.internetReachability}
                      onValueChange={(value) => {
                        if (value) {
                          onDraftChange((current) => ({
                            ...current,
                            internetReachability: value,
                          }))
                        }
                      }}
                    >
                      <SelectTrigger className="h-11 w-full rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] px-3 shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent className="rounded-xl border-border/80 bg-popover/98 backdrop-blur">
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
            </div>
          </FormSection>

          <FormSection
            title="Impact Weighting"
            description="Set how confidentiality, integrity, and availability should influence the effective severity for devices using this profile."
          >
            <div className="grid gap-5 xl:grid-cols-3">
              <FieldBlock
                label={securityProfileFieldGuidance.confidentialityRequirement.label}
                description={securityProfileFieldGuidance.confidentialityRequirement.description}
                helper={securityProfileRequirementHelp[draft.confidentialityRequirement]}
                control={(
                  <Select
                    value={draft.confidentialityRequirement}
                    onValueChange={(value) => {
                      if (value) {
                        onDraftChange((current) => ({
                          ...current,
                          confidentialityRequirement: value,
                        }))
                      }
                    }}
                  >
                    <SelectTrigger className="h-11 w-full rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] px-3 shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent className="rounded-xl border-border/80 bg-popover/98 backdrop-blur">
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
                helper={securityProfileRequirementHelp[draft.integrityRequirement]}
                control={(
                  <Select
                    value={draft.integrityRequirement}
                    onValueChange={(value) => {
                      if (value) {
                        onDraftChange((current) => ({
                          ...current,
                          integrityRequirement: value,
                        }))
                      }
                    }}
                  >
                    <SelectTrigger className="h-11 w-full rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] px-3 shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent className="rounded-xl border-border/80 bg-popover/98 backdrop-blur">
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
                helper={securityProfileRequirementHelp[draft.availabilityRequirement]}
                control={(
                  <Select
                    value={draft.availabilityRequirement}
                    onValueChange={(value) => {
                      if (value) {
                        onDraftChange((current) => ({
                          ...current,
                          availabilityRequirement: value,
                        }))
                      }
                    }}
                  >
                    <SelectTrigger className="h-11 w-full rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] px-3 shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent className="rounded-xl border-border/80 bg-popover/98 backdrop-blur">
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
          </FormSection>

          <FormSection
            title="Environmental Overrides"
            description="These explicit CVSS environmental fields are authoritative. Use Not Defined to inherit the vendor CVSS metric for that dimension."
          >
            <div className="grid gap-5 xl:grid-cols-2">
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedAttackVector.label}
                description={securityProfileFieldGuidance.modifiedAttackVector.description}
                helper={securityProfileModifiedAttackVectorHelp[draft.modifiedAttackVector]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedAttackVector}
                    options={securityProfileModifiedAttackVectorOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedAttackVector: value as SecurityProfileDraft['modifiedAttackVector'] }))}
                  />
                )}
              />
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedAttackComplexity.label}
                description={securityProfileFieldGuidance.modifiedAttackComplexity.description}
                helper={securityProfileModifiedAttackComplexityHelp[draft.modifiedAttackComplexity]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedAttackComplexity}
                    options={securityProfileModifiedAttackComplexityOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedAttackComplexity: value as SecurityProfileDraft['modifiedAttackComplexity'] }))}
                  />
                )}
              />
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedPrivilegesRequired.label}
                description={securityProfileFieldGuidance.modifiedPrivilegesRequired.description}
                helper={securityProfileModifiedPrivilegesRequiredHelp[draft.modifiedPrivilegesRequired]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedPrivilegesRequired}
                    options={securityProfileModifiedPrivilegesRequiredOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedPrivilegesRequired: value as SecurityProfileDraft['modifiedPrivilegesRequired'] }))}
                  />
                )}
              />
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedUserInteraction.label}
                description={securityProfileFieldGuidance.modifiedUserInteraction.description}
                helper={securityProfileModifiedUserInteractionHelp[draft.modifiedUserInteraction]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedUserInteraction}
                    options={securityProfileModifiedUserInteractionOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedUserInteraction: value as SecurityProfileDraft['modifiedUserInteraction'] }))}
                  />
                )}
              />
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedScope.label}
                description={securityProfileFieldGuidance.modifiedScope.description}
                helper={securityProfileModifiedScopeHelp[draft.modifiedScope]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedScope}
                    options={securityProfileModifiedScopeOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedScope: value as SecurityProfileDraft['modifiedScope'] }))}
                  />
                )}
              />
            </div>
            <div className="grid gap-5 xl:grid-cols-3">
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedConfidentialityImpact.label}
                description={securityProfileFieldGuidance.modifiedConfidentialityImpact.description}
                helper={securityProfileModifiedImpactHelp[draft.modifiedConfidentialityImpact]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedConfidentialityImpact}
                    options={securityProfileModifiedImpactOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedConfidentialityImpact: value as SecurityProfileDraft['modifiedConfidentialityImpact'] }))}
                  />
                )}
              />
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedIntegrityImpact.label}
                description={securityProfileFieldGuidance.modifiedIntegrityImpact.description}
                helper={securityProfileModifiedImpactHelp[draft.modifiedIntegrityImpact]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedIntegrityImpact}
                    options={securityProfileModifiedImpactOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedIntegrityImpact: value as SecurityProfileDraft['modifiedIntegrityImpact'] }))}
                  />
                )}
              />
              <FieldBlock
                label={securityProfileFieldGuidance.modifiedAvailabilityImpact.label}
                description={securityProfileFieldGuidance.modifiedAvailabilityImpact.description}
                helper={securityProfileModifiedImpactHelp[draft.modifiedAvailabilityImpact]}
                control={(
                  <ProfileSelect
                    value={draft.modifiedAvailabilityImpact}
                    options={securityProfileModifiedImpactOptions}
                    onValueChange={(value) => onDraftChange((current) => ({ ...current, modifiedAvailabilityImpact: value as SecurityProfileDraft['modifiedAvailabilityImpact'] }))}
                  />
                )}
              />
            </div>
          </FormSection>

          <FormSection
            title="CVSS Preview"
            description="Use the calculator to see how this profile adjusts a vendor CVSS vector into an environmental score."
          >
            <CvssWorkbenchTrigger
              securityProfile={{
                name: draft.name.trim() || null,
                internetReachability: draft.internetReachability,
                confidentialityRequirement: draft.confidentialityRequirement,
                integrityRequirement: draft.integrityRequirement,
                availabilityRequirement: draft.availabilityRequirement,
                modifiedAttackVector: draft.modifiedAttackVector,
                modifiedAttackComplexity: draft.modifiedAttackComplexity,
                modifiedPrivilegesRequired: draft.modifiedPrivilegesRequired,
                modifiedUserInteraction: draft.modifiedUserInteraction,
                modifiedScope: draft.modifiedScope,
                modifiedConfidentialityImpact: draft.modifiedConfidentialityImpact,
                modifiedIntegrityImpact: draft.modifiedIntegrityImpact,
                modifiedAvailabilityImpact: draft.modifiedAvailabilityImpact,
              }}
              title="Environmental scoring workbench"
              description="Preview how this security profile changes a vendor CVSS vector, then open the full calculator only when you need to inspect the detailed metric breakdown."
            />
          </FormSection>
        </div>

        <SheetFooter className="border-t border-border/60">
          <InsetPanel emphasis="subtle" className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
            <p className="text-sm text-muted-foreground">
              Profiles can be assigned later from the asset inspector or the asset detail page.
            </p>
            <Button disabled={!canSave || isSaving} onClick={onSave}>
              {isSaving ? 'Saving...' : saveLabel}
            </Button>
          </InsetPanel>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  )
}

function ProfileSelect({
  value,
  options,
  onValueChange,
}: {
  value: string
  options: readonly string[]
  onValueChange: (value: string | null) => void
}) {
  return (
    <Select value={value} onValueChange={onValueChange}>
      <SelectTrigger className="h-11 w-full rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)] px-3 shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
        <SelectValue />
      </SelectTrigger>
      <SelectContent className="rounded-xl border-border/80 bg-popover/98 backdrop-blur">
        {options.map((option) => (
          <SelectItem key={option} value={option}>
            {option}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
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
  const tooltipText = [description, helper].filter(Boolean).join('\n\n')

  return (
    <div className="grid content-start gap-2">
      <div className="flex min-h-5 items-center gap-2">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        {tooltipText ? (
          <Tooltip>
            <TooltipTrigger className="inline-flex items-center text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
              <CircleHelp className="size-3.5" />
            </TooltipTrigger>
            <TooltipContent
              align="start"
              side="top"
              className="max-w-sm rounded-lg border border-border/80 bg-popover px-3 py-2 text-xs leading-5 text-popover-foreground shadow-lg"
            >
              <div className="space-y-2">
                {description ? <p>{description}</p> : null}
                {helper ? <p className="text-primary/90">{helper}</p> : null}
              </div>
            </TooltipContent>
          </Tooltip>
        ) : null}
      </div>
      {control}
    </div>
  )
}

function FormSection({
  title,
  description,
  children,
}: {
  title: string
  description: string
  children: React.ReactNode
}) {
  return (
    <div className="space-y-5">
      <div className="space-y-1">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{title}</p>
        <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{description}</p>
      </div>
      {children}
      <Separator className="opacity-60" />
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
