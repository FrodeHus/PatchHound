import { useState, useCallback } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { ArrowLeft, CircleHelp, PenSquare, Plus, Trash2 } from 'lucide-react'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { createSecurityProfile, deleteSecurityProfile, fetchSecurityProfiles, updateSecurityProfile } from '@/api/security-profiles.functions'
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
  securityProfileFieldGuidance,
  securityProfileInternetReachabilityHelp,
  securityProfileInternetReachabilityOptions,
  securityProfileRequirementHelp,
  securityProfileRequirementOptions,
} from '@/lib/options/security-profiles'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/platform/security-profiles')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: baseListSearchSchema.extend({
    mode: z.enum(['new', 'edit']).optional(),
    profileId: z.string().optional(),
  }),
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
  const [draft, setDraft] = useState<SecurityProfileDraft>(defaultDraft)
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)
  const tenantNames = new Map(tenants.map((tenant) => [tenant.id, tenant.name]))
  const canViewAudit = (user.activeRoles ?? []).includes('GlobalAdmin') || (user.activeRoles ?? []).includes('Auditor')

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
        environmentClass: editingProfile?.environmentClass ?? 'Workstation',
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
      toast.success('Security profile saved')
      await queryClient.invalidateQueries({ queryKey: ['security-profiles'] })
      if (canViewAudit) {
        await queryClient.invalidateQueries({ queryKey: ['audit-log', 'AssetSecurityProfile', selectedTenantId] })
      }
      closeEditor()
    },
    onError: () => {
      toast.error('Failed to save security profile')
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      await deleteSecurityProfile({ data: { id } })
    },
    onSuccess: async () => {
      toast.success('Security profile deleted')
      setPendingDeleteId(null)
      await queryClient.invalidateQueries({ queryKey: ['security-profiles'] })
      if (canViewAudit) {
        await queryClient.invalidateQueries({ queryKey: ['audit-log', 'AssetSecurityProfile', selectedTenantId] })
      }
    },
    onError: () => {
      toast.error('Failed to delete security profile. It may be assigned to assets.')
      setPendingDeleteId(null)
    },
  })

  const profilePage = profilesQuery.data
  const recentAuditItems = recentAuditQuery.data?.items ?? []
  const canSave = !!selectedTenantId?.trim() && draft.name.trim().length > 0
  const editingProfileId = search.mode === 'edit' ? search.profileId ?? null : null
  const isCreateMode = search.mode === 'new'
  const isEditorOpen = isCreateMode || !!editingProfileId
  const editingProfile =
    profilePage?.items.find((item) => item.id === editingProfileId) ?? null
  const sheetTitle = editingProfile ? 'Edit security profile' : 'Create security profile'
  const sheetDescription = editingProfile
    ? 'Update the severity context for this reusable tenant profile.'
    : 'Define a reusable environment profile for the tenant selected in the top bar.'
  const selectedTenantName = tenantNames.get(selectedTenantId ?? '') ?? 'No tenant selected'

  function openNewProfile() {
    setDraft(defaultDraft())
    void navigate({
      to: '/admin/platform/security-profiles',
      search: (prev) => ({ ...prev, mode: 'new', profileId: undefined }),
    })
  }

  function openProfile(profile: SecurityProfile) {
    setDraft(toDraft(profile))
    void navigate({
      to: '/admin/platform/security-profiles',
      search: (prev) => ({ ...prev, mode: 'edit', profileId: profile.id }),
    })
  }

  function closeEditor() {
    void navigate({
      to: '/admin/platform/security-profiles',
      search: ({ mode: _, profileId: _p, ...prev }) => prev,
    })
  }

  const confirmDelete = useCallback((id: string) => {
    setPendingDeleteId(id)
  }, [])

  const executeDelete = useCallback(() => {
    if (pendingDeleteId) {
      deleteMutation.mutate(pendingDeleteId)
    }
  }, [pendingDeleteId, deleteMutation])

  return (
    <TooltipProvider>
      <section className="space-y-4 pb-4">
        {isEditorOpen ? (
          <SecurityProfileEditorPage
            title={sheetTitle}
            description={sheetDescription}
            selectedTenantName={selectedTenantName}
            editingProfile={editingProfile}
            draft={draft}
            isSaving={saveMutation.isPending}
            canSave={canSave}
            onDraftChange={setDraft}
            onSave={() => saveMutation.mutate()}
            onBack={closeEditor}
          />
        ) : (
          <>
        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardHeader>
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <p className="mb-1 text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  Platform configuration
                </p>
                <CardTitle>Security Profiles</CardTitle>
                <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                  Environment profiles adjust CVSS environmental severity based on reachability, business impact, and
                  operational context.
                </p>
              </div>
              <div className="flex items-center gap-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  {profilePage?.totalCount ?? 0} total
                </p>
                <Button
                  type="button"
                  disabled={!selectedTenantId}
                  onClick={openNewProfile}
                >
                  <Plus className="size-4" />
                  New profile
                </Button>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {!selectedTenantId ? (
              <InsetPanel className="flex items-center justify-between gap-4 px-4 py-6 text-sm text-muted-foreground">
                <span>Choose a tenant from the top bar to review and edit security profiles.</span>
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
                <Button type="button" onClick={openNewProfile}>
                  <Plus className="size-4" />
                  Create profile
                </Button>
              </InsetPanel>
            ) : (
              <div className="rounded-xl border border-border/60 overflow-hidden">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border/60 bg-muted/40">
                      <th className="px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground">Name</th>
                      <th className="px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground hidden md:table-cell">Reachability</th>
                      <th className="px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground hidden lg:table-cell">C / I / A</th>
                      <th className="px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground hidden xl:table-cell">Overrides</th>
                      <th className="px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground hidden sm:table-cell">Updated</th>
                      <th className="w-24 px-4 py-2.5" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/40">
                    {profilePage?.items.map((profile) => (
                      <ProfileRow
                        key={profile.id}
                        profile={profile}
                        isPendingDelete={pendingDeleteId === profile.id}
                        isDeleting={deleteMutation.isPending && pendingDeleteId === profile.id}
                        onEdit={() => openProfile(profile)}
                        onDelete={() => confirmDelete(profile.id)}
                        onConfirmDelete={executeDelete}
                        onCancelDelete={() => setPendingDeleteId(null)}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {profilePage ? (
              <div className="mt-3">
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
              </div>
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
          </>
        )}
      </section>
    </TooltipProvider>
  )
}

function ProfileRow({
  profile,
  isPendingDelete,
  isDeleting,
  onEdit,
  onDelete,
  onConfirmDelete,
  onCancelDelete,
}: {
  profile: SecurityProfile
  isPendingDelete: boolean
  isDeleting: boolean
  onEdit: () => void
  onDelete: () => void
  onConfirmDelete: () => void
  onCancelDelete: () => void
}) {
  const overrideCount = countOverrides(profile)

  return (
    <tr className="group transition-colors hover:bg-muted/30">
      <td className="px-4 py-3">
        <div>
          <p className="font-medium text-foreground">{profile.name}</p>
          {profile.description ? (
            <p className="mt-0.5 text-xs text-muted-foreground line-clamp-1">{profile.description}</p>
          ) : null}
        </div>
      </td>
      <td className="px-4 py-3 hidden md:table-cell">
        <Badge variant="outline" className="rounded-full border-border/80 text-xs">
          {profile.internetReachability}
        </Badge>
      </td>
      <td className="px-4 py-3 hidden lg:table-cell">
        <div className="flex gap-1.5">
          <RequirementPill label="C" value={profile.confidentialityRequirement} />
          <RequirementPill label="I" value={profile.integrityRequirement} />
          <RequirementPill label="A" value={profile.availabilityRequirement} />
        </div>
      </td>
      <td className="px-4 py-3 hidden xl:table-cell">
        {overrideCount > 0 ? (
          <span className="text-xs text-muted-foreground">{overrideCount} override{overrideCount !== 1 ? 's' : ''}</span>
        ) : (
          <span className="text-xs text-muted-foreground/50">None</span>
        )}
      </td>
      <td className="px-4 py-3 hidden sm:table-cell">
        <span className="text-xs text-muted-foreground">
          {new Date(profile.updatedAt).toLocaleDateString()}
        </span>
      </td>
      <td className="px-4 py-3">
        {isPendingDelete ? (
          <div className="flex items-center gap-1.5">
            <Button variant="destructive" size="sm" onClick={onConfirmDelete} disabled={isDeleting}>
              {isDeleting ? 'Deleting...' : 'Confirm'}
            </Button>
            <Button variant="ghost" size="sm" onClick={onCancelDelete} disabled={isDeleting}>
              Cancel
            </Button>
          </div>
        ) : (
          <div className="flex items-center justify-end gap-1 opacity-0 transition-opacity group-hover:opacity-100">
            <Button variant="ghost" size="icon" className="size-8" onClick={onEdit} title="Edit">
              <PenSquare className="size-3.5" />
            </Button>
            <Button variant="ghost" size="icon" className="size-8 text-destructive" onClick={onDelete} title="Delete">
              <Trash2 className="size-3.5" />
            </Button>
          </div>
        )}
      </td>
    </tr>
  )
}

function RequirementPill({ label, value }: { label: string; value: string }) {
  const colorClass =
    value === 'High'
      ? 'bg-orange-500/15 text-orange-400 border-orange-500/30'
      : value === 'Low'
        ? 'bg-blue-500/15 text-blue-400 border-blue-500/30'
        : 'bg-muted/60 text-muted-foreground border-border/60'

  return (
    <span className={`inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11px] font-medium ${colorClass}`}>
      {label}:{value}
    </span>
  )
}

function countOverrides(profile: SecurityProfile): number {
  let count = 0
  if (profile.modifiedAttackVector !== 'NotDefined') count++
  if (profile.modifiedAttackComplexity !== 'NotDefined') count++
  if (profile.modifiedPrivilegesRequired !== 'NotDefined') count++
  if (profile.modifiedUserInteraction !== 'NotDefined') count++
  if (profile.modifiedScope !== 'NotDefined') count++
  if (profile.modifiedConfidentialityImpact !== 'NotDefined') count++
  if (profile.modifiedIntegrityImpact !== 'NotDefined') count++
  if (profile.modifiedAvailabilityImpact !== 'NotDefined') count++
  return count
}

function SecurityProfileEditorPage({
  title,
  description,
  selectedTenantName,
  editingProfile,
  draft,
  isSaving,
  canSave,
  onDraftChange,
  onSave,
  onBack,
}: {
  title: string
  description: string
  selectedTenantName: string
  editingProfile: SecurityProfile | null
  draft: SecurityProfileDraft
  isSaving: boolean
  canSave: boolean
  onDraftChange: React.Dispatch<React.SetStateAction<SecurityProfileDraft>>
  onSave: () => void
  onBack: () => void
}) {
  const saveLabel = editingProfile ? 'Save changes' : 'Create profile'

  return (
    <div className="space-y-5">
      <div className="space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-1">
            <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
            <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{description}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={onBack}>
              Cancel
            </Button>
            <Button disabled={!canSave || isSaving} onClick={onSave}>
              {isSaving ? 'Saving...' : saveLabel}
            </Button>
          </div>
        </div>
      </div>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_320px]">
        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardContent className="space-y-6 p-5">
          <div className="space-y-6">
            <FormSection
              title="Identity"
              description="Name the profile and confirm the tenant scope before defining how severity should be adjusted."
            >
              <div className="grid gap-5 md:grid-cols-2">
                <FieldBlock
                  label="Active Tenant"
                  description="This form follows the tenant scope in the top navigation."
                  control={<div className="rounded-lg border border-border/70 bg-muted/55 px-3 py-3 text-sm font-medium">{selectedTenantName}</div>}
                />
                <FieldBlock
                  label="Profile Name"
                  description="Use a clear operational name such as &quot;Internet-facing server&quot; or &quot;Internal workstation&quot;."
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
              description="Keep only the settings that actually change the environmental CVSS score for this profile."
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
          </CardContent>
        </Card>

          <div className="space-y-5">
            <InsetPanel className="rounded-2xl border-border/70 bg-card px-4 py-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Readable Summary</p>
              <div className="mt-4 space-y-3">
                <GuideRow label="Tenant" value={selectedTenantName} />
                <GuideRow label="Reachability" value={humanizeReachability(draft.internetReachability)} />
                <GuideRow label="Impact priority" value={summarizeRequirements(draft)} />
              </div>
            </InsetPanel>

            <InsetPanel className="rounded-2xl border-border/70 bg-card px-4 py-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Override Translation</p>
              <div className="mt-4 space-y-3">
                {buildOverrideNarratives(draft).map((item) => (
                  <div key={item.label} className="rounded-xl border border-border/60 bg-background px-3 py-3">
                    <p className="text-sm font-medium text-foreground">{item.label}</p>
                    <p className="mt-1 text-sm text-muted-foreground">{item.text}</p>
                  </div>
                ))}
              </div>
            </InsetPanel>
          </div>
      </div>
    </div>
  )
}

function GuideRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-3 rounded-xl border border-border/60 bg-background px-3 py-3">
      <span className="text-sm font-medium text-foreground">{label}</span>
      <span className="max-w-[14rem] text-right text-xs text-muted-foreground">{value}</span>
    </div>
  )
}

function humanizeReachability(value: SecurityProfileDraft['internetReachability']) {
  switch (value) {
    case 'Internet':
      return 'Directly reachable from the internet.'
    case 'InternalNetwork':
      return 'Reachable only from the internal network.'
    case 'AdjacentOnly':
      return 'Reachable only from the same segment or adjacent network.'
    case 'LocalOnly':
      return 'Requires local presence or an existing foothold.'
  }
}

function summarizeRequirements(draft: SecurityProfileDraft) {
  const priorities = [
    draft.confidentialityRequirement === 'High' ? 'confidentiality' : null,
    draft.integrityRequirement === 'High' ? 'integrity' : null,
    draft.availabilityRequirement === 'High' ? 'availability' : null,
  ].filter(Boolean)

  if (priorities.length === 0) {
    return 'Balanced weighting across confidentiality, integrity, and availability.'
  }

  return `Emphasizes ${priorities.join(', ')}.`
}

function buildOverrideNarratives(draft: SecurityProfileDraft) {
  return [
    {
      label: 'Attack path',
      text:
        draft.modifiedAttackVector === 'NotDefined'
          ? `Reachability is described as "${humanizeReachability(draft.internetReachability)}" but CVSS attack vector still inherits the vendor metric.`
          : `If this device is ${humanizeReachabilityShort(draft.internetReachability)}, modified attack vector is set to ${draft.modifiedAttackVector.toLowerCase()}.`,
    },
    {
      label: 'Exploit conditions',
      text:
        draft.modifiedAttackComplexity === 'NotDefined'
          ? 'Attack complexity still inherits the vendor metric.'
          : `Exploit preconditions are treated as ${draft.modifiedAttackComplexity.toLowerCase()} complexity in this environment.`,
    },
    {
      label: 'Access assumptions',
      text:
        draft.modifiedPrivilegesRequired === 'NotDefined' && draft.modifiedUserInteraction === 'NotDefined'
          ? 'Privileges required and user interaction both inherit the vendor metrics.'
          : [
              draft.modifiedPrivilegesRequired !== 'NotDefined'
                ? `privileges required is set to ${draft.modifiedPrivilegesRequired.toLowerCase()}`
                : null,
              draft.modifiedUserInteraction !== 'NotDefined'
                ? `user interaction is set to ${draft.modifiedUserInteraction.toLowerCase()}`
                : null,
            ]
              .filter(Boolean)
              .join(', ') + '.',
    },
    {
      label: 'Blast radius',
      text:
        draft.modifiedScope === 'NotDefined'
          ? 'Scope still inherits the vendor metric.'
          : `Scope is treated as ${draft.modifiedScope.toLowerCase()}, which ${draft.modifiedScope === 'Changed' ? 'assumes impact can cross a security boundary' : 'keeps impact within the original security boundary'}.`,
    },
    {
      label: 'Impact overrides',
      text: summarizeImpactOverrides(draft),
    },
  ] as const
}

function humanizeReachabilityShort(value: SecurityProfileDraft['internetReachability']) {
  switch (value) {
    case 'Internet':
      return 'internet reachable'
    case 'InternalNetwork':
      return 'exposed to the local network only'
    case 'AdjacentOnly':
      return 'limited to an adjacent or segmented network'
    case 'LocalOnly':
      return 'only accessible locally'
  }
}

function summarizeImpactOverrides(draft: SecurityProfileDraft) {
  const items = [
    draft.modifiedConfidentialityImpact !== 'NotDefined'
      ? `confidentiality impact is ${draft.modifiedConfidentialityImpact.toLowerCase()}`
      : null,
    draft.modifiedIntegrityImpact !== 'NotDefined'
      ? `integrity impact is ${draft.modifiedIntegrityImpact.toLowerCase()}`
      : null,
    draft.modifiedAvailabilityImpact !== 'NotDefined'
      ? `availability impact is ${draft.modifiedAvailabilityImpact.toLowerCase()}`
      : null,
  ].filter(Boolean)

  return items.length > 0
    ? `${items.join(', ')}.`
    : 'Confidentiality, integrity, and availability impacts all inherit the vendor metrics.'
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
