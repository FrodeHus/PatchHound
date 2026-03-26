import { Link, createFileRoute, notFound } from '@tanstack/react-router'
import { fetchRemediationTasks } from '@/api/remediation-tasks.functions'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { formatDate, startCase } from '@/lib/formatting'

export const Route = createFileRoute('/_authed/remediation/task/$id')({
  loader: async ({ params }) => {
    const tasks = await fetchRemediationTasks({
      data: {
        taskId: params.id,
        page: 1,
        pageSize: 1,
      },
    })

    const task = tasks.items[0]
    if (!task) {
      throw notFound()
    }

    return { task }
  },
  component: RemediationTaskDetailRoute,
})

function RemediationTaskDetailRoute() {
  const { task } = Route.useLoaderData()

  return (
    <section className="space-y-5">
      <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="space-y-3">
          <div className="text-sm text-muted-foreground">
            <Link
              to="/remediation/tasks"
              search={{
                page: 1,
                pageSize: 25,
                search: '',
                vendor: '',
                criticality: '',
                assetOwner: '',
                taskId: task.id,
                tenantSoftwareId: '',
                deviceAssetId: '',
              }}
              className="underline decoration-border/70 underline-offset-4 hover:decoration-foreground"
            >
              Remediation tasks
            </Link>
            <span className="mx-2">/</span>
            <span>{startCase(task.softwareName)}</span>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
              {task.status}
            </Badge>
            <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
              {task.ownerTeamName}
            </Badge>
            <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
              {task.highestDeviceCriticality}
            </Badge>
          </div>
          <div>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">Remediation task</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              Focused execution view for {startCase(task.softwareName)} across the devices assigned to {task.ownerTeamName}.
            </p>
          </div>
        </div>
      </header>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(20rem,0.9fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <CardTitle>{startCase(task.softwareName)}</CardTitle>
            <CardDescription>
              {task.softwareVendor ?? 'Unknown vendor'} software patching task assigned to {task.ownerTeamName}.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <Metric label="Affected devices" value={String(task.affectedDeviceCount)} />
              <Metric label="High or worse" value={String(task.highOrWorseDeviceCount)} />
              <Metric label="Critical links" value={String(task.criticalDeviceCount)} />
              <Metric label="Due" value={formatDate(task.dueDate)} />
            </div>

            <div className="space-y-2">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Device examples</p>
              {task.deviceNames.length === 0 ? (
                <p className="text-sm text-muted-foreground">No linked devices were returned for this task.</p>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {task.deviceNames.map((deviceName: string) => (
                    <Badge key={deviceName} variant="outline" className="rounded-full border-border/70 bg-background/50">
                      {deviceName}
                    </Badge>
                  ))}
                </div>
              )}
            </div>

            <div className="space-y-2">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Known asset owners</p>
              {task.assetOwners.length === 0 ? (
                <p className="text-sm text-muted-foreground">No owner names are currently attached to the linked devices.</p>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {task.assetOwners.map((owner: string) => (
                    <Badge key={owner} variant="outline" className="rounded-full border-border/70 bg-background/50">
                      {owner}
                    </Badge>
                  ))}
                </div>
              )}
            </div>

            <div className="flex flex-wrap gap-2">
              {task.tenantSoftwareId ? (
                <Button
                  render={
                    <Link
                      to="/software/$id"
                      params={{ id: task.tenantSoftwareId }}
                      search={{ page: 1, pageSize: 25, version: '', tab: 'remediation' }}
                    />
                  }
                >
                  Open software remediation
                </Button>
              ) : null}
              <Button
                variant="outline"
                render={
                  <Link
                    to="/remediation/tasks"
                    search={{
                      page: 1,
                      pageSize: 25,
                      search: '',
                      vendor: '',
                      criticality: '',
                      assetOwner: '',
                      taskId: task.id,
                      tenantSoftwareId: '',
                      deviceAssetId: '',
                    }}
                  />
                }
              >
                Show in workbench
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <CardTitle>Task status</CardTitle>
            <CardDescription>
              The current operational state of this remediation task.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <Metric label="Status" value={task.status} />
            <Metric label="Created" value={formatDate(task.createdAt)} />
            <Metric label="Updated" value={formatDate(task.updatedAt)} />
            <Metric label="Responsible team" value={task.ownerTeamName} />
          </CardContent>
        </Card>
      </div>
    </section>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-sm font-medium">{value}</p>
    </div>
  )
}
