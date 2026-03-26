import { useCallback, useEffect, useMemo, useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import type { FilterGroup } from '@/api/asset-rules.schemas'
import { bulkAssignAssets, fetchAssets } from '@/api/assets.functions'
import { fetchTenants } from '@/api/settings.functions'
import type { TeamMembershipRulePreview } from '@/api/teams.schemas'
import { createTeam, fetchTeamDetail, fetchTeams, previewTeamMembershipRule, updateTeamMembers, updateTeamMembershipRule } from '@/api/teams.functions'
import { fetchUsers } from '@/api/users.functions'
import { AssignmentGroupDetailView } from '@/components/features/admin/AssignmentGroupDetailView'
import { CreateTeamDialog } from '@/components/features/admin/CreateTeamDialog'
import { TeamTable } from '@/components/features/admin/TeamTable'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/teams')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps, context }) => {
    const isGlobalAdmin = context.user?.roles.includes('GlobalAdmin') ?? false
    const [teams, tenants] = await Promise.all([
      fetchTeams({ data: { page: deps.page, pageSize: deps.pageSize } }),
      isGlobalAdmin ? fetchTenants({ data: { page: 1, pageSize: 100 } }) : Promise.resolve({ items: [] }),
    ])

    return {
      teams,
      tenants: tenants.items,
    }
  },
  component: TeamsPage,
})

const emptyRuleFilter: FilterGroup = {
  type: 'group',
  operator: 'AND',
  conditions: [],
}

function TeamsPage() {
  const router = useRouter()
  const queryClient = useQueryClient()
  const data = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { user } = Route.useRouteContext()
  const [createState, setCreateState] = useState<'idle' | 'success' | 'error'>('idle')
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(data.teams.items[0]?.id ?? null)
  const [detailState, setDetailState] = useState<'idle' | 'error'>('idle')
  const [filters, setFilters] = useState({
    search: '',
    assetType: '',
    criticality: '',
  })
  const [assetPage, setAssetPage] = useState(1)
  const [assetPageSize, setAssetPageSize] = useState(25)
  const [selectedAssetIds, setSelectedAssetIds] = useState<string[]>([])
  const [memberSearch, setMemberSearch] = useState('')
  const [selectedMemberId, setSelectedMemberId] = useState('')
  const [isDynamic, setIsDynamic] = useState(false)
  const [ruleFilter, setRuleFilter] = useState<FilterGroup>(emptyRuleFilter)
  const [rulePreview, setRulePreview] = useState<TeamMembershipRulePreview | null>(null)
  const [isDynamicConfirmOpen, setIsDynamicConfirmOpen] = useState(false)
  const canManageGroup = user.roles.includes('GlobalAdmin')
  const teamsQuery = useQuery({
    queryKey: ['teams', search.page, search.pageSize],
    queryFn: () => fetchTeams({ data: { page: search.page, pageSize: search.pageSize } }),
    initialData: data.teams,
  })

  // Use first team as fallback when nothing is explicitly selected
  const effectiveTeamId = selectedTeamId ?? teamsQuery.data.items[0]?.id ?? null

  const createMutation = useMutation({
    mutationFn: async (payload: { name: string; tenantId: string }) => {
      await createTeam({ data: payload })
    },
    onMutate: () => {
      setCreateState('idle')
    },
    onSuccess: async () => {
      setCreateState('success')
      toast.success('Assignment group created')
      await router.invalidate()
    },
    onError: () => {
      setCreateState('error')
      toast.error('Failed to create assignment group')
    },
  })

  const teamDetailQuery = useQuery({
    queryKey: ['team-detail', effectiveTeamId],
    queryFn: () => fetchTeamDetail({ data: { teamId: effectiveTeamId! } }),
    enabled: Boolean(effectiveTeamId),
  })

  useEffect(() => {
    if (teamDetailQuery.error) {
      setDetailState('error')
      toast.error('Failed to load assignment group details')
    }
  }, [teamDetailQuery.error])

  useEffect(() => {
    const nextRule = teamDetailQuery.data?.membershipRule
    setIsDynamic(teamDetailQuery.data?.isDynamic ?? false)
    if (nextRule?.filterDefinition?.type === 'group') {
      setRuleFilter(nextRule.filterDefinition as FilterGroup)
    } else {
      setRuleFilter(emptyRuleFilter)
    }
    setRulePreview(null)
  }, [teamDetailQuery.data?.id, teamDetailQuery.data?.membershipRule])

  const assetsQuery = useQuery({
    queryKey: [
      'team-detail-assets',
      teamDetailQuery.data?.tenantId,
      filters.search,
      filters.assetType,
      filters.criticality,
      assetPage,
      assetPageSize,
    ],
    queryFn: () =>
      fetchAssets({
        data: {
          tenantId: teamDetailQuery.data!.tenantId,
          search: filters.search || undefined,
          assetType: filters.assetType || undefined,
          criticality: filters.criticality || undefined,
          page: assetPage,
          pageSize: assetPageSize,
        },
      }),
    enabled: Boolean(teamDetailQuery.data?.tenantId),
  })

  const usersQuery = useQuery({
    queryKey: ['team-member-candidates', teamDetailQuery.data?.tenantId],
    queryFn: () =>
      fetchUsers({
        data: {
          page: 1,
          pageSize: 500,
          status: 'Enabled',
        },
      }),
    enabled: canManageGroup && Boolean(teamDetailQuery.data?.tenantId),
  })

  const assignAssetsMutation = useMutation({
    mutationFn: async (payload: { assetIds: string[]; teamId: string }) =>
      bulkAssignAssets({
        data: {
          assetIds: payload.assetIds,
          ownerType: 'Team',
          ownerId: payload.teamId,
        },
      }),
    onSuccess: async () => {
      setSelectedAssetIds([])
      toast.success('Assets assigned')
      await router.invalidate()
    },
    onError: () => {
      toast.error('Failed to assign assets')
    },
  })

  const updateMembersMutation = useMutation({
    mutationFn: async (payload: { userId: string; action: 'add' | 'remove' }) =>
      updateTeamMembers({
        data: {
          teamId: effectiveTeamId!,
          ...payload,
        },
      }),
    onSuccess: async (_, variables) => {
      setSelectedMemberId('')
      toast.success(variables.action === 'add' ? 'Member added' : 'Member removed')
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['team-detail', effectiveTeamId] }),
        queryClient.invalidateQueries({ queryKey: ['teams'] }),
        queryClient.invalidateQueries({ queryKey: ['team-member-candidates'] }),
      ])
    },
    onError: () => {
      toast.error('Failed to update group membership')
    },
  })

  const previewRuleMutation = useMutation({
    mutationFn: async () =>
      previewTeamMembershipRule({
        data: {
          teamId: effectiveTeamId!,
          isDynamic,
          acknowledgeMemberReset: false,
          filterDefinition: ruleFilter,
        },
      }),
    onSuccess: (data) => {
      setRulePreview(data)
    },
    onError: () => {
      toast.error('Failed to preview membership rule')
    },
  })

  const saveRuleMutation = useMutation({
    mutationFn: async () =>
      updateTeamMembershipRule({
        data: {
          teamId: effectiveTeamId!,
          isDynamic,
          acknowledgeMemberReset: false,
          filterDefinition: ruleFilter,
        },
      }),
    onSuccess: async () => {
      toast.success('Membership rule saved')
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['team-detail', effectiveTeamId] }),
        queryClient.invalidateQueries({ queryKey: ['teams'] }),
      ])
    },
    onError: async (error) => {
      const message = error instanceof Error ? error.message : 'Failed to save membership rule'
      if (message.includes('will remove all current members')) {
        setIsDynamicConfirmOpen(true)
        return
      }

      toast.error(message)
    },
  })

  const availableMembers = useMemo(() => {
    const currentMemberIds = new Set(teamDetailQuery.data?.members.map((member) => member.userId) ?? [])
    const pool = usersQuery.data?.items ?? []
    const query = memberSearch.trim().toLowerCase()

    return pool
      .filter((userItem) => !currentMemberIds.has(userItem.id))
      .filter((userItem) => {
        if (!query) {
          return true
        }

        return (
          userItem.displayName.toLowerCase().includes(query)
          || userItem.email.toLowerCase().includes(query)
          || (userItem.company ?? '').toLowerCase().includes(query)
        )
      })
      .sort((left, right) => left.displayName.localeCompare(right.displayName))
  }, [memberSearch, teamDetailQuery.data?.members, usersQuery.data?.items])

  useEffect(() => {
    if (!selectedMemberId) {
      return
    }

    if (!availableMembers.some((member) => member.id === selectedMemberId)) {
      setSelectedMemberId('')
    }
  }, [availableMembers, selectedMemberId])

  const selectTeam = useCallback((teamId: string | null) => {
    setSelectedTeamId(teamId)
    setDetailState('idle')
    setAssetPage(1)
    setSelectedAssetIds([])
    setMemberSearch('')
    setSelectedMemberId('')
    setRulePreview(null)
  }, [])

  return (
    <section className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-[-0.04em]">Assignment Groups</h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            Organize operational ownership by tenant, then manage members, dynamic rules, and asset assignment from a single workspace.
          </p>
        </div>
        {canManageGroup ? (
          <CreateTeamDialog
            isSubmitting={createMutation.isPending}
            tenants={data.tenants.map((tenant) => ({ id: tenant.id, name: tenant.name }))}
            onCreate={(payload) => {
              createMutation.mutate(payload)
            }}
          />
        ) : null}
      </div>
      {createState === 'success' ? (
        <p className="text-sm text-tone-success-foreground">Assignment group created.</p>
      ) : null}
      {createState === 'error' ? (
        <p className="text-sm text-destructive">Failed to create assignment group.</p>
      ) : null}
      <div className="grid gap-4 xl:grid-cols-[22rem_minmax(0,1fr)]">
        <TeamTable
          teams={teamsQuery.data.items}
          totalCount={teamsQuery.data.totalCount}
          page={teamsQuery.data.page}
          pageSize={teamsQuery.data.pageSize}
          totalPages={teamsQuery.data.totalPages}
          selectedTeamId={selectedTeamId}
          onPageChange={(page) => {
            void navigate({
              search: (prev) => ({ ...prev, page }),
            })
          }}
          onPageSizeChange={(nextPageSize) => {
            void navigate({
              search: (prev) => ({ ...prev, pageSize: nextPageSize, page: 1 }),
            })
            setSelectedTeamId(null)
          }}
          onSelectTeam={selectTeam}
        />
        <div className="min-w-0">
          {detailState === 'error' ? (
            <div className="rounded-[2rem] border border-destructive/30 bg-destructive/5 px-5 py-4 text-sm text-destructive">
              Failed to load assignment group details.
            </div>
          ) : null}
          {teamDetailQuery.data ? (
            <AssignmentGroupDetailView
              team={teamDetailQuery.data}
              canManageGroup={canManageGroup}
              availableMembers={availableMembers}
              selectedMemberId={selectedMemberId}
              memberSearch={memberSearch}
              assets={assetsQuery.data?.items ?? []}
              totalAssetCount={assetsQuery.data?.totalCount ?? 0}
              assetPage={assetsQuery.data?.page ?? assetPage}
              assetPageSize={assetsQuery.data?.pageSize ?? assetPageSize}
              assetTotalPages={assetsQuery.data?.totalPages ?? 0}
              selectedAssetIds={selectedAssetIds}
              filters={filters}
              isLoadingAssets={teamDetailQuery.isLoading || assetsQuery.isLoading}
              isAssigningAssets={assignAssetsMutation.isPending}
              isUpdatingMembers={updateMembersMutation.isPending}
              isPreviewingRule={previewRuleMutation.isPending}
              isSavingRule={saveRuleMutation.isPending}
              onMemberSearchChange={setMemberSearch}
              onSelectedMemberChange={setSelectedMemberId}
              onAddMember={() => {
                if (!selectedMemberId) {
                  return
                }

                updateMembersMutation.mutate({
                  userId: selectedMemberId,
                  action: 'add',
                })
              }}
              onRemoveMember={(userId) => {
                updateMembersMutation.mutate({
                  userId,
                  action: 'remove',
                })
              }}
              ruleFilter={ruleFilter}
              isDynamic={isDynamic}
              rulePreview={rulePreview}
              onRuleFilterChange={setRuleFilter}
              onDynamicChange={setIsDynamic}
              onPreviewRule={() => {
                previewRuleMutation.mutate()
              }}
              onSaveRule={() => {
                saveRuleMutation.mutate()
              }}
              onFilterChange={(next) => {
                setFilters(next)
                setAssetPage(1)
              }}
              onAssetPageChange={setAssetPage}
              onAssetPageSizeChange={(nextPageSize) => {
                setAssetPageSize(nextPageSize)
                setAssetPage(1)
              }}
              onToggleAsset={(assetId) => {
                setSelectedAssetIds((current) =>
                  current.includes(assetId)
                    ? current.filter((id) => id !== assetId)
                    : [...current, assetId],
                )
              }}
              onToggleAllVisible={() => {
                const visibleIds = (assetsQuery.data?.items ?? []).map((asset) => asset.id)
                const allVisibleSelected = visibleIds.every((id) => selectedAssetIds.includes(id))
                setSelectedAssetIds((current) =>
                  allVisibleSelected
                    ? current.filter((id) => !visibleIds.includes(id))
                    : Array.from(new Set([...current, ...visibleIds])),
                )
              }}
              onAssignSelected={() => {
                if (!canManageGroup || !selectedTeamId || selectedAssetIds.length === 0) {
                  return
                }

                assignAssetsMutation.mutate({
                  assetIds: selectedAssetIds,
                  teamId: selectedTeamId,
                })
              }}
            />
          ) : (
            <div className="flex min-h-[32rem] items-center justify-center rounded-[2rem] border border-dashed border-border/70 bg-card/60 px-8 text-center">
              <div className="max-w-md space-y-2">
                <p className="text-xs uppercase tracking-[0.22em] text-muted-foreground">Workspace</p>
                <h2 className="text-2xl font-semibold tracking-[-0.04em]">Select an assignment group</h2>
                <p className="text-sm leading-6 text-muted-foreground">
                  Open a group from the directory to manage membership, dynamic rules, and bulk asset assignment.
                </p>
              </div>
            </div>
          )}
        </div>
      </div>
      <Dialog open={isDynamicConfirmOpen} onOpenChange={setIsDynamicConfirmOpen}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>Enable dynamic membership?</DialogTitle>
            <DialogDescription>
              This assignment group currently has manually added members. Enabling dynamic membership will remove all current members before applying the rule.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setIsDynamicConfirmOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={async () => {
                try {
                  await updateTeamMembershipRule({
                    data: {
                      teamId: effectiveTeamId!,
                      isDynamic,
                      acknowledgeMemberReset: true,
                      filterDefinition: ruleFilter,
                    },
                  })
                  setIsDynamicConfirmOpen(false)
                  toast.success('Dynamic membership enabled')
                  await Promise.all([
                    queryClient.invalidateQueries({ queryKey: ['team-detail', effectiveTeamId] }),
                    queryClient.invalidateQueries({ queryKey: ['teams'] }),
                  ])
                } catch {
                  toast.error('Failed to enable dynamic membership')
                }
              }}
            >
              Enable dynamic membership
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}
