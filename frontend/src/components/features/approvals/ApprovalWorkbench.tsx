import { Link } from '@tanstack/react-router'
import { Eye } from 'lucide-react'
import type { PagedApprovalTaskList } from '@/api/approval-tasks.schemas'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { toneBadge } from '@/lib/tone-classes'
import { formatDate, startCase } from '@/lib/formatting'
import { ApprovalStatusBadge, ApprovalTypeBadge } from './ApprovalBadge'
import { ApprovalExpiryCountdown } from './ApprovalExpiryCountdown'
import type {
  ApprovalWorkbenchConfig,
  ApprovalWorkbenchFilters,
} from './approval-workbench-config'

type Props = {
  config: ApprovalWorkbenchConfig
  data: PagedApprovalTaskList
  filters: ApprovalWorkbenchFilters
  onFiltersChange: (filters: ApprovalWorkbenchFilters) => void
  onPageChange: (page: number) => void
  onMarkRead: (id: string) => void
}

function severityTone(severity: string) {
  switch (severity) {
    case 'Critical':
      return 'danger' as const
    case 'High':
      return 'warning' as const
    case 'Medium':
      return 'neutral' as const
    case 'Low':
      return 'info' as const
    default:
      return 'neutral' as const
  }
}

function buildDetailRoute(route: string, item: { id: string; remediationCaseId: string }) {
  return route
    .replace('$caseId', item.remediationCaseId)
    .replace('$id', item.id)
}

export function ApprovalWorkbench({
  config,
  data,
  filters,
  onFiltersChange,
  onPageChange,
  onMarkRead,
}: Props) {
  return (
    <section className="space-y-5">
      <header className="rounded-lg border border-border/70 bg-card px-5 py-4">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="min-w-0 space-y-2">
            <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
              {config.eyebrow}
            </p>
            <h1 className="text-2xl font-semibold leading-tight">{config.title}</h1>
            <p className="max-w-4xl text-sm leading-relaxed text-muted-foreground">
              {config.description}
            </p>
          </div>
          <div className="grid gap-2 sm:grid-cols-3 xl:min-w-[32rem]">
            {config.metrics.map((metric) => (
              <Metric
                key={metric.label}
                label={metric.label}
                value={metric.value(data.items, data.totalCount)}
              />
            ))}
          </div>
        </div>
      </header>

      <section className="rounded-lg border border-border/70 bg-card">
        <div className="grid gap-3 border-b border-border/70 p-4 lg:grid-cols-[minmax(0,1.4fr)_repeat(2,minmax(0,1fr))_auto]">
          <Input
            placeholder={config.searchPlaceholder}
            value={filters.search}
            onChange={(event) =>
              onFiltersChange({ ...filters, search: event.target.value })
            }
          />
          <Select
            value={filters.status || '__all__'}
            onValueChange={(value) =>
              onFiltersChange({
                ...filters,
                status: value && value !== '__all__' ? value : '',
              })
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Status" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">All statuses</SelectItem>
              <SelectItem value="Pending">Pending review</SelectItem>
              <SelectItem value="Approved">Approved</SelectItem>
              <SelectItem value="Denied">Denied</SelectItem>
              <SelectItem value="AutoApproved">Auto-approved</SelectItem>
              <SelectItem value="AutoDenied">Expired</SelectItem>
            </SelectContent>
          </Select>
          <Select
            value={filters.type || '__all__'}
            onValueChange={(value) =>
              onFiltersChange({
                ...filters,
                type: value && value !== '__all__' ? value : '',
              })
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Type" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">All types</SelectItem>
              {config.typeOptions.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <label className="flex cursor-pointer items-center gap-2 self-center whitespace-nowrap text-sm text-muted-foreground">
            <Checkbox
              checked={filters.showRead}
              onCheckedChange={(checked) =>
                onFiltersChange({ ...filters, showRead: checked === true })
              }
            />
            Show read
          </label>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-border/70 text-sm">
            <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
              <tr>
                <th className="px-4 py-3">Software</th>
                <th className="px-4 py-3">Decision</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Severity</th>
                <th className="px-4 py-3">Maintenance window</th>
                <th className="px-4 py-3">Expiry</th>
                <th className="px-4 py-3">Created</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border/60 bg-background">
              {data.items.length === 0 ? (
                <tr>
                  <td className="px-4 py-6 text-sm text-muted-foreground" colSpan={8}>
                    {config.emptyText}
                  </td>
                </tr>
              ) : (
                data.items.map((item) => (
                  <tr key={item.id} className={`align-top ${item.readAt ? 'opacity-60' : ''}`}>
                    <td className="px-4 py-3">
                      <div className="space-y-1">
                        <Link
                          to={buildDetailRoute(config.detailRoute, item)}
                          className="font-medium hover:text-primary"
                        >
                          {startCase(item.softwareName)}
                        </Link>
                        <div className="flex gap-1.5">
                          <span
                            className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(item.criticality))}`}
                          >
                            {item.criticality}
                          </span>
                          <span className="text-[10px] text-muted-foreground">
                            {item.vulnerabilityCount} vulns
                          </span>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="space-y-1">
                        <ApprovalTypeBadge type={item.type} />
                        <p className="text-xs text-muted-foreground">
                          {startCase(item.outcome)} by {item.decidedByName}
                        </p>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <ApprovalStatusBadge status={item.status} />
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(item.highestSeverity))}`}
                      >
                        {item.highestSeverity}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {item.maintenanceWindowDate ? formatDate(item.maintenanceWindowDate) : 'Not scheduled'}
                    </td>
                    <td className="px-4 py-3">
                      <ApprovalExpiryCountdown expiresAt={item.expiresAt} compact />
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">
                      {formatDate(item.createdAt)}
                    </td>
                    <td className="px-4 py-3">
                      {item.status !== 'Pending' && !item.readAt ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => onMarkRead(item.id)}
                          title="Mark as read"
                        >
                          <Eye className="size-4" />
                        </Button>
                      ) : null}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-border/70 p-4">
          <p className="text-sm text-muted-foreground">
            Page {data.page} of {Math.max(data.totalPages, 1)}
          </p>
          <div className="flex gap-2">
            <Button
              variant="outline"
              disabled={data.page <= 1}
              onClick={() => onPageChange(data.page - 1)}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              disabled={data.page >= Math.max(data.totalPages, 1)}
              onClick={() => onPageChange(data.page + 1)}
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
    <div className="rounded-md border border-border/70 bg-background px-3 py-2">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium">{value}</p>
    </div>
  )
}
