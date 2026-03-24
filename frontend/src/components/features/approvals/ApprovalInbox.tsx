import { Link } from '@tanstack/react-router'
import type { PagedApprovalTaskList } from '@/api/approval-tasks.schemas'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Checkbox } from '@/components/ui/checkbox'
import { toneBadge } from '@/lib/tone-classes'
import { formatDate, startCase } from '@/lib/formatting'
import { ApprovalTypeBadge, ApprovalStatusBadge } from './ApprovalBadge'
import { ApprovalExpiryCountdown } from './ApprovalExpiryCountdown'
import { Eye } from 'lucide-react'

type Filters = {
  status: string
  type: string
  search: string
  showRead: boolean
}

type Props = {
  data: PagedApprovalTaskList
  filters: Filters
  onFiltersChange: (filters: Filters) => void
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

export function ApprovalInbox({
  data,
  filters,
  onFiltersChange,
  onPageChange,
  onMarkRead,
}: Props) {
  const pendingCount = data.items.filter((i) => i.status === 'Pending').length

  return (
    <section className="space-y-5">
      <header className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Approval inbox
            </p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">
              Remediation approvals
            </h1>
            <p className="max-w-3xl text-sm text-muted-foreground">
              Review and action pending remediation approval tasks. Auto-approved
              and informational items can be marked as read.
            </p>
          </div>
          <div className="grid min-w-[220px] gap-3 rounded-xl border border-border/70 bg-background/50 p-4">
            <Metric label="Pending approval" value={String(pendingCount)} />
            <Metric label="Total tasks" value={String(data.totalCount)} />
          </div>
        </div>
      </header>

      <section className="rounded-2xl border border-border/70 bg-card p-5">
        <div className="grid gap-3 lg:grid-cols-[minmax(0,1.4fr)_repeat(2,minmax(0,1fr))_auto]">
          <Input
            placeholder="Search software name"
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
              <SelectItem value="RiskAcceptanceApproval">
                Risk exception approval
              </SelectItem>
              <SelectItem value="PatchingApproved">Patch decision review</SelectItem>
              <SelectItem value="PatchingDeferred">Deferred patching notice</SelectItem>
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

        <div className="mt-5 overflow-hidden rounded-xl border border-border/70">
          <table className="min-w-full divide-y divide-border/70 text-sm">
            <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
              <tr>
                <th className="px-4 py-3">Software</th>
                <th className="px-4 py-3">Type</th>
                <th className="px-4 py-3">Status</th>
                <th className="px-4 py-3">Severity</th>
                <th className="px-4 py-3">Expiry</th>
                <th className="px-4 py-3">Created</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border/60 bg-background">
              {data.items.length === 0 ? (
                <tr>
                  <td
                    className="px-4 py-6 text-sm text-muted-foreground"
                    colSpan={7}
                  >
                    No approval tasks match the current filters.
                  </td>
                </tr>
              ) : (
                data.items.map((item) => (
                  <tr
                    key={item.id}
                    className={`align-top ${item.readAt ? 'opacity-60' : ''}`}
                  >
                    <td className="px-4 py-3">
                      <div className="space-y-1">
                        <Link
                          to="/approvals/$id"
                          params={{ id: item.id }}
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
                      <ApprovalTypeBadge type={item.type} />
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
                    <td className="px-4 py-3">
                      <ApprovalExpiryCountdown
                        expiresAt={item.expiresAt}
                        compact
                      />
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

        <div className="mt-4 flex items-center justify-between gap-3">
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
    <div className="rounded-2xl border border-border/70 bg-background px-4 py-3">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </p>
      <p className="mt-2 text-sm font-medium">{value}</p>
    </div>
  )
}
