import { useEffect, useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { bulkAssignAssets, fetchAssets } from '@/api/assets.functions'
import { fetchTenants } from '@/api/settings.functions'
import { createTeam, fetchTeamDetail, fetchTeams } from '@/api/teams.functions'
import { AssignmentGroupDetailView } from '@/components/features/admin/AssignmentGroupDetailView'
import { CreateTeamDialog } from '@/components/features/admin/CreateTeamDialog'
import { TeamTable } from '@/components/features/admin/TeamTable'

export const Route = createFileRoute('/_authed/admin/teams')({
  loader: async () => {
    const [teams, tenants] = await Promise.all([
      fetchTeams({ data: {} }),
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
  const [createState, setCreateState] = useState<'idle' | 'success' | 'error'>('idle')
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(data.teams.items[0]?.id ?? null)
  const [detailState, setDetailState] = useState<'idle' | 'error'>('idle')
  const [filters, setFilters] = useState({
    search: '',
    assetType: '',
    criticality: '',
  })
  const [selectedAssetIds, setSelectedAssetIds] = useState<string[]>([])

  const createMutation = useMutation({
    mutationFn: async (payload: { name: string; tenantId: string }) => {
      await createTeam({ data: payload })
    },
    onMutate: () => {
      setCreateState('idle')
    },
    onSuccess: async () => {
      setCreateState('success')
      await router.invalidate()
    },
    onError: () => {
      setCreateState('error')
    },
  })

  const detailMutation = useMutation({
    mutationFn: async (teamId: string) => fetchTeamDetail({ data: { teamId } }),
    onError: () => {
      setDetailState('error')
    },
  })

  const assetsMutation = useMutation({
    mutationFn: async (payload: { tenantId: string; search: string; assetType: string; criticality: string }) =>
      fetchAssets({
        data: {
          tenantId: payload.tenantId,
          search: payload.search || undefined,
          assetType: payload.assetType || undefined,
          criticality: payload.criticality || undefined,
          page: 1,
          pageSize: 100,
        },
      }),
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
      if (selectedTeamId) {
        await detailMutation.mutateAsync(selectedTeamId)
      }
      await router.invalidate()
    },
  })

  useEffect(() => {
    if (!selectedTeamId) {
      return
    }

    setDetailState('idle')
    void detailMutation.mutateAsync(selectedTeamId)
  }, [selectedTeamId])

  useEffect(() => {
    if (!detailMutation.data?.tenantId) {
      return
    }

    setSelectedAssetIds([])
    void assetsMutation.mutateAsync({
      tenantId: detailMutation.data.tenantId,
      search: filters.search,
      assetType: filters.assetType,
      criticality: filters.criticality,
    })
  }, [detailMutation.data?.tenantId, filters.search, filters.assetType, filters.criticality])

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
        <p className="text-sm text-emerald-300">Assignment group created.</p>
      ) : null}
      {createState === 'error' ? (
        <p className="text-sm text-destructive">Failed to create assignment group.</p>
      ) : null}
      <TeamTable
        teams={data.teams.items}
        totalCount={data.teams.totalCount}
        selectedTeamId={selectedTeamId}
        onSelectTeam={setSelectedTeamId}
      />
      {detailState === 'error' ? (
        <p className="text-sm text-destructive">Failed to load assignment group details.</p>
      ) : null}
      {detailMutation.data ? (
        <AssignmentGroupDetailView
          team={detailMutation.data}
          assets={assetsMutation.data?.items ?? []}
          totalAssetCount={assetsMutation.data?.totalCount ?? 0}
          selectedAssetIds={selectedAssetIds}
          filters={filters}
          isLoadingAssets={detailMutation.isPending || assetsMutation.isPending}
          isAssigningAssets={assignAssetsMutation.isPending}
          onFilterChange={setFilters}
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
