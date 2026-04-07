import { createFileRoute, redirect } from '@tanstack/react-router'
import { z } from 'zod'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ScanRunnersTab } from '@/components/features/admin/scan-runners/ScanRunnersTab'
import { ConnectionProfilesTab } from '@/components/features/admin/connection-profiles/ConnectionProfilesTab'
import { ScanningToolsTab } from '@/components/features/admin/scanning-tools/ScanningToolsTab'
import { ScanProfilesTab } from '@/components/features/admin/scan-profiles/ScanProfilesTab'
import {
  fetchConnectionProfiles,
  fetchScanProfiles,
  fetchScanRunners,
  fetchScanningTool,
  fetchScanningTools,
  fetchToolVersions,
} from '@/api/authenticated-scans.functions'
import type { ScanningToolVersion } from '@/api/authenticated-scans.schemas'
import { baseListSearchSchema } from '@/routes/-list-search'

const tabValues = ['profiles', 'tools', 'connections', 'runners'] as const

export const Route = createFileRoute('/_authed/admin/authenticated-scans')({
  beforeLoad: ({ context }) => {
    if (!context.user?.featureFlags.authenticatedScans) {
      throw redirect({ to: '/admin' })
    }

    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('CustomerAdmin') && !activeRoles.includes('GlobalAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: baseListSearchSchema.extend({
    tab: z.enum(tabValues).optional(),
    toolId: z.string().uuid().optional(),
  }),
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    const tab = deps.tab ?? 'profiles'
    const paging = { page: deps.page, pageSize: deps.pageSize }

    // Only fetch data for the active tab
    if (tab === 'profiles') {
      const [profiles, tools, connections, runners] = await Promise.all([
        fetchScanProfiles({ data: paging }),
        fetchScanningTools({ data: { pageSize: 100 } }),
        fetchConnectionProfiles({ data: { pageSize: 100 } }),
        fetchScanRunners({ data: { pageSize: 100 } }),
      ])
      return { profiles, tools, connections, runners, tab }
    }
    if (tab === 'tools') {
      const tools = await fetchScanningTools({ data: paging })
      if (deps.toolId) {
        const [tool, versions] = await Promise.all([
          fetchScanningTool({ data: { id: deps.toolId } }),
          fetchToolVersions({ data: { toolId: deps.toolId } }),
        ])
        const currentScript = versions.find((version: ScanningToolVersion) =>
          version.id === tool.currentVersionId
        )?.scriptContent ?? ''
        return { tools, toolDetail: tool, toolVersions: versions, currentScript, tab }
      }
      return { tools, tab }
    }
    if (tab === 'connections') {
      return { connections: await fetchConnectionProfiles({ data: paging }), tab }
    }
    return { runners: await fetchScanRunners({ data: paging }), tab }
  },
  component: AuthenticatedScansWorkbench,
})

function AuthenticatedScansWorkbench() {
  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  const loaderData = Route.useLoaderData()
  const activeTab = search.tab ?? 'profiles'

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Authenticated Scans</h1>
        <p className="text-muted-foreground text-sm">
          Manage scan profiles, tools, connections, and runners for on-prem host scanning.
        </p>
      </div>

      <Tabs
        value={activeTab}
        onValueChange={(value) =>
          navigate({ search: { tab: value as (typeof tabValues)[number], page: 1, pageSize: search.pageSize } })
        }
      >
        <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
          <TabsTrigger value="profiles">Scan Profiles</TabsTrigger>
          <TabsTrigger value="tools">Scanning Tools</TabsTrigger>
          <TabsTrigger value="connections">Connections</TabsTrigger>
          <TabsTrigger value="runners">Runners</TabsTrigger>
        </TabsList>

        <TabsContent value="profiles" className="space-y-4 pt-1">
          {'profiles' in loaderData && loaderData.profiles && (
            <ScanProfilesTab
              initialData={loaderData.profiles}
              runners={loaderData.runners?.items ?? []}
              connections={loaderData.connections?.items ?? []}
              tools={loaderData.tools?.items ?? []}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
              onPageSizeChange={(ps) => navigate({ search: { ...search, page: 1, pageSize: ps } })}
            />
          )}
        </TabsContent>

        <TabsContent value="tools" className="space-y-4 pt-1">
          {'tools' in loaderData && loaderData.tools && (
            <ScanningToolsTab
              initialData={loaderData.tools}
              toolDetail={'toolDetail' in loaderData ? loaderData.toolDetail : undefined}
              currentScript={'currentScript' in loaderData ? (loaderData.currentScript as string) : undefined}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
              onPageSizeChange={(ps) => navigate({ search: { ...search, page: 1, pageSize: ps } })}
              onSelectTool={(id) => navigate({ search: { ...search, toolId: id } })}
              onDeselectTool={() => navigate({ search: { ...search, toolId: undefined } })}
            />
          )}
        </TabsContent>

        <TabsContent value="connections" className="space-y-4 pt-1">
          {'connections' in loaderData && loaderData.connections && (
            <ConnectionProfilesTab
              initialData={loaderData.connections}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
              onPageSizeChange={(ps) => navigate({ search: { ...search, page: 1, pageSize: ps } })}
            />
          )}
        </TabsContent>

        <TabsContent value="runners" className="space-y-4 pt-1">
          {'runners' in loaderData && loaderData.runners && (
            <ScanRunnersTab
              initialData={loaderData.runners}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
              onPageSizeChange={(ps) => navigate({ search: { ...search, page: 1, pageSize: ps } })}
            />
          )}
        </TabsContent>
      </Tabs>
    </div>
  )
}
