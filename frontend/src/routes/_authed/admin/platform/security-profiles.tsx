import { useCallback, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, Link, Outlet, redirect, useRouterState } from '@tanstack/react-router'
import { toast } from 'sonner'
import { PenSquare, Plus, Trash2 } from 'lucide-react'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { deleteSecurityProfile, fetchSecurityProfiles } from '@/api/security-profiles.functions'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/platform/security-profiles')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) =>
    fetchSecurityProfiles({
      data: {
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: SecurityProfilesPage,
})

function SecurityProfilesPage() {
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { user } = Route.useRouteContext()
  const initialProfiles = Route.useLoaderData()
  const queryClient = useQueryClient()
  const { selectedTenantId, tenants } = useTenantScope()
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)
  const tenantNames = new Map(tenants.map((tenant) => [tenant.id, tenant.name]))
  const selectedTenantName = tenantNames.get(selectedTenantId ?? '') ?? 'No tenant selected'
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

  const confirmDelete = useCallback((id: string) => {
    setPendingDeleteId(id)
  }, [])

  const executeDelete = useCallback(() => {
    if (pendingDeleteId) {
      deleteMutation.mutate(pendingDeleteId)
    }
  }, [pendingDeleteId, deleteMutation])

  if (pathname !== '/admin/platform/security-profiles') {
    return <Outlet />
  }

  return (
    <section className="space-y-4 pb-4">
      <Card className="rounded-2xl border-border/70 bg-card/85">
        <CardHeader>
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <p className="mb-1 text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Platform configuration
              </p>
              <CardTitle>Security Profiles</CardTitle>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                Environment profiles adjust CVSS v3.1 environmental severity using asset reachability, business impact,
                and deployment context.
              </p>
            </div>
            <div className="flex items-center gap-3">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                {profilePage?.totalCount ?? 0} total
              </p>
              <Button render={<Link to="/admin/platform/security-profiles/new" search={{ page: 1, pageSize: 25 }} />} disabled={!selectedTenantId}>
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
              <Button render={<Link to="/admin/platform/security-profiles/new" search={{ page: 1, pageSize: 25 }} />}>
                <Plus className="size-4" />
                Create profile
              </Button>
            </InsetPanel>
          ) : (
            <div className="overflow-hidden rounded-xl border border-border/60">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border/60 bg-muted/40">
                    <th className="px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground">Name</th>
                    <th className="hidden px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground md:table-cell">Reachability</th>
                    <th className="hidden px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground lg:table-cell">C / I / A</th>
                    <th className="hidden px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground xl:table-cell">Overrides</th>
                    <th className="hidden px-4 py-2.5 text-left text-xs font-medium uppercase tracking-wider text-muted-foreground sm:table-cell">Updated</th>
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
    </section>
  )
}

function ProfileRow({
  profile,
  isPendingDelete,
  isDeleting,
  onDelete,
  onConfirmDelete,
  onCancelDelete,
}: {
  profile: SecurityProfile
  isPendingDelete: boolean
  isDeleting: boolean
  onDelete: () => void
  onConfirmDelete: () => void
  onCancelDelete: () => void
}) {
  const overrideCount = countOverrides(profile)

  return (
    <tr className="group transition-colors hover:bg-muted/30">
      <td className="px-4 py-3">
        <div>
          <Link
            to="/admin/platform/security-profiles/$profileId"
            params={{ profileId: profile.id }}
            search={{ page: 1, pageSize: 25 }}
            className="font-medium text-primary hover:underline"
          >
            {profile.name}
          </Link>
          {profile.description ? (
            <p className="mt-0.5 line-clamp-1 text-xs text-muted-foreground">{profile.description}</p>
          ) : null}
        </div>
      </td>
      <td className="hidden px-4 py-3 md:table-cell">
        <Badge variant="outline" className="rounded-full border-border/80 text-xs">
          {profile.internetReachability}
        </Badge>
      </td>
      <td className="hidden px-4 py-3 lg:table-cell">
        <div className="flex gap-1.5">
          <RequirementPill label="C" value={profile.confidentialityRequirement} />
          <RequirementPill label="I" value={profile.integrityRequirement} />
          <RequirementPill label="A" value={profile.availabilityRequirement} />
        </div>
      </td>
      <td className="hidden px-4 py-3 xl:table-cell">
        {overrideCount > 0 ? (
          <span className="text-xs text-muted-foreground">{overrideCount} override{overrideCount !== 1 ? 's' : ''}</span>
        ) : (
          <span className="text-xs text-muted-foreground/50">None</span>
        )}
      </td>
      <td className="hidden px-4 py-3 sm:table-cell">
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
            <Button
              variant="ghost"
              size="icon"
              className="size-8"
              title="Edit"
              render={<Link to="/admin/platform/security-profiles/$profileId" params={{ profileId: profile.id }} search={{ page: 1, pageSize: 25 }} />}
            >
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
      ? 'border-orange-500/30 bg-orange-500/15 text-orange-400'
      : value === 'Low'
        ? 'border-blue-500/30 bg-blue-500/15 text-blue-400'
        : 'border-border/60 bg-muted/60 text-muted-foreground'

  return (
    <span className={`inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11px] font-medium ${colorClass}`}>
      {label}:{value}
    </span>
  )
}

function countOverrides(profile: SecurityProfile): number {
  return [
    profile.modifiedAttackVector,
    profile.modifiedAttackComplexity,
    profile.modifiedPrivilegesRequired,
    profile.modifiedUserInteraction,
    profile.modifiedScope,
    profile.modifiedConfidentialityImpact,
    profile.modifiedIntegrityImpact,
    profile.modifiedAvailabilityImpact,
  ].filter((value) => value !== 'NotDefined').length
}
