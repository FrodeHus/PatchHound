import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import {
  fetchNormalizedSoftwareDetail,
  fetchNormalizedSoftwareInstallations,
  fetchNormalizedSoftwareVulnerabilities,
} from '@/api/software.functions'
import { SoftwareDetailPage } from '@/components/features/software/SoftwareDetailPage'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { softwareQueryKeys } from '@/features/software/list-state'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'

const softwareDetailSearchSchema = baseListSearchSchema.extend({
  version: searchStringSchema,
})

export const Route = createFileRoute('/_authed/software/$id')({
  validateSearch: softwareDetailSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ params, deps }) => {
    const detail = await fetchNormalizedSoftwareDetail({ data: { id: params.id } })
    const selectedVersion = deps.version || normalizeVersion(detail.versionCohorts[0]?.version ?? null)
    const [installations, vulnerabilities] = await Promise.all([
      fetchNormalizedSoftwareInstallations({
        data: {
          id: params.id,
          version: selectedVersion || undefined,
          activeOnly: true,
          page: deps.page,
          pageSize: deps.pageSize,
        },
      }),
      fetchNormalizedSoftwareVulnerabilities({ data: { id: params.id } }),
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
  const { selectedTenantId } = useTenantScope()

  const detailQuery = useQuery({
    queryKey: softwareQueryKeys.detail(selectedTenantId, id),
    queryFn: () => fetchNormalizedSoftwareDetail({ data: { id } }),
    initialData: initialData.detail,
  })

  const selectedVersion =
    search.version || normalizeVersion(detailQuery.data.versionCohorts[0]?.version ?? null)

  const installationsQuery = useQuery({
    queryKey: softwareQueryKeys.installations(selectedTenantId, id, selectedVersion, search.page, search.pageSize),
    queryFn: () =>
      fetchNormalizedSoftwareInstallations({
        data: {
          id,
          version: selectedVersion || undefined,
          activeOnly: true,
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
    initialData:
      initialData.selectedVersion === selectedVersion &&
      initialData.installations.page === search.page &&
      initialData.installations.pageSize === search.pageSize
        ? initialData.installations
        : undefined,
  })

  const vulnerabilitiesQuery = useQuery({
    queryKey: softwareQueryKeys.vulnerabilities(selectedTenantId, id),
    queryFn: () => fetchNormalizedSoftwareVulnerabilities({ data: { id } }),
    initialData: initialData.vulnerabilities,
  })

  const installations = installationsQuery.data ?? initialData.installations
  const vulnerabilities = vulnerabilitiesQuery.data ?? initialData.vulnerabilities

  return (
    <SoftwareDetailPage
      detail={detailQuery.data}
      selectedVersion={selectedVersion}
      installations={installations}
      vulnerabilities={vulnerabilities}
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
    />
  )
}

function normalizeVersion(version: string | null) {
  return version?.trim() ?? ''
}
