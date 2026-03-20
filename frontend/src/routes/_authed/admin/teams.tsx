import { useCallback, useEffect, useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { bulkAssignAssets, fetchAssets } from '@/api/assets.functions'
import { fetchTenants } from '@/api/settings.functions'
import { createTeam, fetchTeamDetail, fetchTeams } from '@/api/teams.functions'
import { AssignmentGroupDetailView } from '@/components/features/admin/AssignmentGroupDetailView'
import { CreateTeamDialog } from '@/components/features/admin/CreateTeamDialog'
import { TeamTable } from '@/components/features/admin/TeamTable'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/teams')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    const [teams, tenants] = await Promise.all([
      fetchTeams({ data: { page: deps.page, pageSize: deps.pageSize } }),
      fetchTenants({ data: { page: 1, pageSize: 100 } }),
    ])

    return {
      teams,
      tenants: tenants.items,
    }
  },
  component: TeamsPage,
})

function TeamsPage() {
  const router = useRouter()
  const data = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
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

  const detailMutation = useMutation({
    mutationFn: async (teamId: string) => fetchTeamDetail({ data: { teamId } }),
    onError: () => {
      setDetailState('error')
      toast.error('Failed to load assignment group details')
    },
  })
  const { mutateAsync: fetchDetailAsync } = detailMutation

  const assetsMutation = useMutation({
    mutationFn: async (payload: {
      tenantId: string
      search: string
      assetType: string
      criticality: string
      page: number
      pageSize: number
    }) =>
      fetchAssets({
        data: {
          tenantId: payload.tenantId,
          search: payload.search || undefined,
          assetType: payload.assetType || undefined,
          criticality: payload.criticality || undefined,
          page: payload.page,
          pageSize: payload.pageSize,
        },
      }),
  })
  const { mutateAsync: fetchAssetsAsync } = assetsMutation

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
      if (effectiveTeamId) {
        await fetchDetailAsync(effectiveTeamId)
      }
      await router.invalidate()
    },
    onError: () => {
      toast.error('Failed to assign assets')
    },
  })

  const selectTeam = useCallback((teamId: string | null) => {
    setSelectedTeamId(teamId)
    if (teamId) {
      setDetailState('idle')
      setAssetPage(1)
      setSelectedAssetIds([])
      void fetchDetailAsync(teamId)
    }
  }, [fetchDetailAsync])

  // Fetch detail for the initial/fallback team selection
  useEffect(() => {
    if (!effectiveTeamId || detailMutation.data || detailMutation.isPending) {
      return
    }
    void fetchDetailAsync(effectiveTeamId)
  }, [effectiveTeamId, detailMutation.data, detailMutation.isPending, fetchDetailAsync])

  // Fetch assets when the detail's tenant or filter/pagination state changes
  useEffect(() => {
    if (!detailMutation.data?.tenantId) {
      return
    }

    void fetchAssetsAsync({
      tenantId: detailMutation.data.tenantId,
      search: filters.search,
      assetType: filters.assetType,
      criticality: filters.criticality,
      page: assetPage,
      pageSize: assetPageSize,
    })
  }, [detailMutation.data?.tenantId, filters.search, filters.assetType, filters.criticality, assetPage, assetPageSize, fetchAssetsAsync])

  return (
    <section className="space-y-4">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold">Assignment Groups</h1>
        <p className="text-sm text-muted-foreground">
          Group users by operational ownership so assets and workflows can be assigned consistently.
        </p>
      </div>
      <CreateTeamDialog
        isSubmitting={createMutation.isPending}
        tenants={data.tenants.map((tenant) => ({ id: tenant.id, name: tenant.name }))}
        onCreate={(payload) => {
          createMutation.mutate(payload)
        }}
      />
      {createState === 'success' ? (
        <p className="text-sm text-tone-success-foreground">Assignment group created.</p>
      ) : null}
      {createState === 'error' ? (
        <p className="text-sm text-destructive">Failed to create assignment group.</p>
      ) : null}
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
      {detailState === 'error' ? (
        <p className="text-sm text-destructive">Failed to load assignment group details.</p>
      ) : null}
      {detailMutation.data ? (
        <AssignmentGroupDetailView
          team={detailMutation.data}
          assets={assetsMutation.data?.items ?? []}
          totalAssetCount={assetsMutation.data?.totalCount ?? 0}
          assetPage={assetsMutation.data?.page ?? assetPage}
          assetPageSize={assetsMutation.data?.pageSize ?? assetPageSize}
          assetTotalPages={assetsMutation.data?.totalPages ?? 0}
          selectedAssetIds={selectedAssetIds}
          filters={filters}
          isLoadingAssets={detailMutation.isPending || assetsMutation.isPending}
          isAssigningAssets={assignAssetsMutation.isPending}
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
            const visibleIds = (assetsMutation.data?.items ?? []).map((asset) => asset.id)
            const allVisibleSelected = visibleIds.every((id) => selectedAssetIds.includes(id))
            setSelectedAssetIds((current) =>
              allVisibleSelected
                ? current.filter((id) => !visibleIds.includes(id))
                : Array.from(new Set([...current, ...visibleIds])),
            )
          }}
          onAssignSelected={() => {
            if (!selectedTeamId || selectedAssetIds.length === 0) {
              return
            }

            assignAssetsMutation.mutate({
              assetIds: selectedAssetIds,
              teamId: selectedTeamId,
            })
          }}
        />
      ) : null}
    </section>
  )
}
