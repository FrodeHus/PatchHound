import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { z } from 'zod'
import {
  assignTenantSoftwareOwner,
  fetchTenantSoftwareDetail,
  fetchTenantSoftwareInstallations,
  fetchTenantSoftwareVulnerabilities,
} from '@/api/software.functions'
import { fetchTenantSoftwareDecisionContext } from '@/api/remediation.functions'
import { fetchTeams } from '@/api/teams.functions'
import { SoftwareDetailPage } from '@/components/features/software/SoftwareDetailPage'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { softwareQueryKeys } from '@/features/software/list-state'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'
import { toast } from 'sonner'

const softwareDetailSearchSchema = baseListSearchSchema.extend({
  version: searchStringSchema,
  tab: z.enum(['overview', 'remediation', 'ai']).catch('overview'),
})

export const Route = createFileRoute('/_authed/software/$id')({
  validateSearch: softwareDetailSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ params, deps }) => {
    const detail = await fetchTenantSoftwareDetail({ data: { id: params.id } })
    const selectedVersion = deps.version || normalizeVersion(detail.versionCohorts[0]?.version ?? null)
    const [installations, vulnerabilities] = await Promise.all([
      fetchTenantSoftwareInstallations({
        data: {
          id: params.id,
          version: selectedVersion || undefined,
          activeOnly: true,
          page: deps.page,
          pageSize: deps.pageSize,
        },
      }),
      fetchTenantSoftwareVulnerabilities({ data: { id: params.id } }),
    ])

    return { detail, installations, vulnerabilities, selectedVersion }
  },
  component: SoftwareDetailRoute,
})

function SoftwareDetailRoute() {
  const { id } = Route.useParams()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const initialData = Route.useLoaderData()
  const { user } = Route.useRouteContext()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const queryClient = useQueryClient()

  const detailQuery = useQuery({
    queryKey: softwareQueryKeys.detail(selectedTenantId, id),
    queryFn: () => fetchTenantSoftwareDetail({ data: { id } }),
    initialData: canUseInitialData ? initialData.detail : undefined,
  })
  const detail = detailQuery.data ?? (canUseInitialData ? initialData.detail : undefined)

  const selectedVersion =
    search.version || normalizeVersion(detail?.versionCohorts[0]?.version ?? null)

  const installationsQuery = useQuery({
    queryKey: softwareQueryKeys.installations(selectedTenantId, id, selectedVersion, search.page, search.pageSize),
    queryFn: () =>
      fetchTenantSoftwareInstallations({
        data: {
          id,
          version: selectedVersion || undefined,
          activeOnly: true,
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
    enabled: Boolean(detail),
    initialData:
      canUseInitialData &&
      initialData.selectedVersion === selectedVersion &&
      initialData.installations.page === search.page &&
      initialData.installations.pageSize === search.pageSize
        ? initialData.installations
        : undefined,
  })

  const vulnerabilitiesQuery = useQuery({
    queryKey: softwareQueryKeys.vulnerabilities(selectedTenantId, id),
    queryFn: () => fetchTenantSoftwareVulnerabilities({ data: { id } }),
    enabled: Boolean(detail),
    initialData: canUseInitialData ? initialData.vulnerabilities : undefined,
  })

  const installations = installationsQuery.data ?? (canUseInitialData ? initialData.installations : undefined)
  const vulnerabilities = vulnerabilitiesQuery.data ?? (canUseInitialData ? initialData.vulnerabilities : undefined)
  const canViewRemediation = (
    (user.activeRoles ?? []).includes('GlobalAdmin')
    || (user.activeRoles ?? []).includes('AssetOwner')
  )

  const remediationQuery = useQuery({
    queryKey: softwareQueryKeys.remediation(selectedTenantId, id),
    queryFn: () => fetchTenantSoftwareDecisionContext({ data: { tenantSoftwareId: id } }),
    enabled: canViewRemediation,
    refetchInterval: (currentQuery) => {
      const status = currentQuery.state.data?.patchAssessment.jobStatus
      return status === 'Pending' || status === 'Running' ? 8000 : false
    },
    refetchIntervalInBackground: false,
  })

  const teamsQuery = useQuery({
    queryKey: ['teams', selectedTenantId],
    queryFn: () => fetchTeams({ data: { tenantId: selectedTenantId ?? undefined, pageSize: 200 } }),
    enabled: Boolean(selectedTenantId),
  })

  const ownerMutation = useMutation({
    mutationFn: (teamId: string | null) => assignTenantSoftwareOwner({ data: { id, teamId } }),
    onSuccess: async (_data, teamId) => {
      toast.success(teamId ? 'Software owner updated.' : 'Software owner returned to rule control.')
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: softwareQueryKeys.all }),
        queryClient.invalidateQueries({ queryKey: ['teams', selectedTenantId] }),
      ])
    },
    onError: () => {
      toast.error('Failed to update software owner.')
    },
  })

  if (!detail || !installations || !vulnerabilities) {
    return null
  }

  return (
    <SoftwareDetailPage
      detail={detail}
      selectedVersion={selectedVersion}
      installations={installations}
      vulnerabilities={vulnerabilities}
      activeTab={search.tab}
      onTabChange={(tab) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            tab,
          }),
        })
      }}
      onSelectVersion={(version) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            version,
            page: 1,
          }),
        })
      }}
      onPageChange={(page) => {
        void navigate({
          search: (prev) => ({
            ...prev,
            page,
          }),
        })
      }}
      canViewRemediation={canViewRemediation}
      remediationData={remediationQuery.data ?? null}
      isRemediationLoading={remediationQuery.isLoading || remediationQuery.isFetching}
      remediationError={remediationQuery.isError}
      tenantSoftwareId={id}
      ownerTeams={teamsQuery.data?.items ?? []}
      onOwnerTeamChange={(teamId) => ownerMutation.mutate(teamId)}
      isOwnerTeamUpdating={ownerMutation.isPending}
    />
  )
}

function normalizeVersion(version: string | null) {
  return version?.trim() ?? ''
}
