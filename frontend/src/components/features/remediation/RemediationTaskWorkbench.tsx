import { Link } from '@tanstack/react-router'
import type { PagedRemediationTasks } from '@/api/remediation-tasks.schemas'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatDate } from '@/lib/formatting'

type Filters = {
  search: string
  vendor: string
  criticality: string
  assetOwner: string
}

type Props = {
  tasks: PagedRemediationTasks
  filters: Filters
  scopedToSoftware: boolean
  scopedToDevice: boolean
  onFiltersChange: (filters: Filters) => void
  onPageChange: (page: number) => void
}

export function RemediationTaskWorkbench({
  tasks,
  filters,
  scopedToSoftware,
  scopedToDevice,
  onFiltersChange,
  onPageChange,
}: Props) {
  return (
    <section className="space-y-5">
      <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Remediation workbench</p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">Open software remediation tasks</h1>
            <p className="max-w-3xl text-sm text-muted-foreground">
              Review the software patching backlog across the tenant. Filter the list to isolate which vendor is driving work, which owners are affected, and where business-critical devices are concentrated.
            </p>
          </div>
          <div className="grid min-w-[220px] gap-3 rounded-xl border border-border/70 bg-background/50 p-4">
            <Metric label="Open tasks" value={String(tasks.totalCount)} />
            <Metric
              label="Scope"
              value={scopedToSoftware ? 'Software filtered' : scopedToDevice ? 'Device filtered' : 'Tenant-wide'}
            />
          </div>
        </div>
      </header>

      <section className="rounded-2xl border border-border/70 bg-card p-5">
        <div className="grid gap-3 lg:grid-cols-[minmax(0,1.4fr)_repeat(3,minmax(0,1fr))]">
          <Input
            placeholder="Search software, vendor, device, or owner"
            value={filters.search}
            onChange={(event) => onFiltersChange({ ...filters, search: event.target.value })}
          />
          <Input
            placeholder="Filter by vendor"
            value={filters.vendor}
            onChange={(event) => onFiltersChange({ ...filters, vendor: event.target.value })}
          />
          <Select
            value={filters.criticality || '__all__'}
            onValueChange={(value) =>
              onFiltersChange({
                ...filters,
                criticality: value && value !== '__all__' ? value : '',
              })
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Criticality" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">All criticalities</SelectItem>
              <SelectItem value="Critical">Critical</SelectItem>
              <SelectItem value="High">High</SelectItem>
              <SelectItem value="Medium">Medium</SelectItem>
              <SelectItem value="Low">Low</SelectItem>
            </SelectContent>
          </Select>
          <Input
            placeholder="Filter by asset owner"
            value={filters.assetOwner}
            onChange={(event) => onFiltersChange({ ...filters, assetOwner: event.target.value })}
          />
        </div>

        <div className="mt-5 overflow-hidden rounded-xl border border-border/70">
          <table className="min-w-full divide-y divide-border/70 text-sm">
            <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
              <tr>
                <th className="px-4 py-3">Software</th>
                <th className="px-4 py-3">Vendor</th>
                <th className="px-4 py-3">Responsible team</th>
                <th className="px-4 py-3">Devices</th>
                <th className="px-4 py-3">Pressure</th>
                <th className="px-4 py-3">Due</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border/60 bg-background">
              {tasks.items.length === 0 ? (
                <tr>
                  <td className="px-4 py-6 text-sm text-muted-foreground" colSpan={6}>
                    No open remediation tasks match the current filters.
                  </td>
                </tr>
              ) : (
                tasks.items.map((task) => (
                  <tr key={task.id} className="align-top">
                    <td className="px-4 py-3">
                      <div className="space-y-1">
                        {task.tenantSoftwareId ? (
                          <Link
                            to="/software/$id"
                            params={{ id: task.tenantSoftwareId }}
                            search={{ page: 1, pageSize: 25, version: '' }}
                            className="font-medium hover:text-primary"
                          >
                            {task.softwareName}
                          </Link>
                        ) : (
                          <span className="font-medium">{task.softwareName}</span>
                        )}
                        <p className="text-xs text-muted-foreground">
                          {task.deviceNames.length > 0
                            ? `Examples: ${task.deviceNames.join(', ')}`
                            : 'No linked devices found'}
                        </p>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {task.softwareVendor ?? 'Unknown'}
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      <div className="space-y-1">
                        <div className="font-medium text-foreground">{task.ownerTeamName}</div>
                        <div className="text-xs">
                          {task.assetOwners.length > 0 ? task.assetOwners.join(', ') : 'No owner names'}
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      <div>{task.affectedDeviceCount} linked</div>
                      <div className="mt-1 text-xs">
                        {task.highOrWorseDeviceCount} high-or-worse
                      </div>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      <div>{task.criticalDeviceCount} critical-device link{task.criticalDeviceCount === 1 ? '' : 's'}</div>
                      <div className="mt-1 text-xs">
                        Highest device criticality: {task.highestDeviceCriticality}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      <div>{formatDate(task.dueDate)}</div>
                      <div className="mt-1 text-xs">{task.status}</div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <div className="mt-4 flex items-center justify-between gap-3">
          <p className="text-sm text-muted-foreground">
            Page {tasks.page} of {Math.max(tasks.totalPages, 1)}
          </p>
          <div className="flex gap-2">
            <Button
              variant="outline"
              disabled={tasks.page <= 1}
              onClick={() => onPageChange(tasks.page - 1)}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              disabled={tasks.page >= Math.max(tasks.totalPages, 1)}
              onClick={() => onPageChange(tasks.page + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      </section>
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
