import { createFileRoute, redirect } from '@tanstack/react-router'
import { z } from 'zod'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  fetchConnectionProfiles,
  fetchScanProfiles,
  fetchScanRunners,
  fetchScanningTools,
} from '@/api/authenticated-scans.functions'
import { baseListSearchSchema } from '@/routes/-list-search'

const tabValues = ['profiles', 'tools', 'connections', 'runners'] as const

export const Route = createFileRoute('/_authed/admin/authenticated-scans')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('CustomerAdmin') && !activeRoles.includes('GlobalAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: baseListSearchSchema.extend({
    tab: z.enum(tabValues).optional(),
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
      return { tools: await fetchScanningTools({ data: paging }), tab }
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
          <p className="text-muted-foreground text-sm">Scan profiles tab — loaded in next task.</p>
        </TabsContent>

        <TabsContent value="tools" className="space-y-4 pt-1">
          <p className="text-muted-foreground text-sm">Scanning tools tab — loaded in next task.</p>
        </TabsContent>

        <TabsContent value="connections" className="space-y-4 pt-1">
          <p className="text-muted-foreground text-sm">Connection profiles tab — loaded in next task.</p>
        </TabsContent>

        <TabsContent value="runners" className="space-y-4 pt-1">
          <p className="text-muted-foreground text-sm">Scan runners tab — loaded in next task.</p>
        </TabsContent>
      </Tabs>
    </div>
  )
}
