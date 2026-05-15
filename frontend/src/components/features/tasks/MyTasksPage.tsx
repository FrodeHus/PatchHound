import { Link } from '@tanstack/react-router'
import { ClipboardList, ShieldAlert } from 'lucide-react'
import type { MyTaskBucket } from '@/api/my-tasks.schemas'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatDate, startCase } from '@/lib/formatting'
import { toneBadge } from '@/lib/tone-classes'
import {
  formatSoftwareOwnerRoutingDetail,
  severityTone,
} from '@/components/features/remediation/remediation-utils'
import { BUCKET_LABELS, type TaskBucketKey } from './my-tasks-buckets'

type MyTasksPageProps = {
  sections: MyTaskBucket[]
  pageSize: number
  onLoadNext: (bucket: TaskBucketKey) => void
  onPageSizeChange: (pageSize: number) => void
}

export function MyTasksPage({
  sections,
  pageSize,
  onLoadNext,
  onPageSizeChange,
}: MyTasksPageProps) {
  const loadedCount = sections.reduce((sum, section) => sum + section.items.length, 0)
  const activeQueueCount = sections.filter((section) => section.items.length > 0 || section.hasMore).length

  return (
    <section className="space-y-5">
      <header className="rounded-[28px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_50%),var(--color-card)] p-5">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Work queue
            </p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">
              My tasks
            </h1>
            <p className="max-w-3xl text-sm text-muted-foreground">
              Remediation cases waiting on your action, grouped by what each role needs to do next.
            </p>
          </div>
          <div className="grid min-w-[260px] gap-3 rounded-xl border border-border/70 bg-background/50 p-4">
            <Metric label="Active queues" value={activeQueueCount.toLocaleString()} />
            <Metric label="Loaded tasks" value={loadedCount.toLocaleString()} />
            <div className="flex items-center gap-2">
              <span className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
                Page size
              </span>
              <Select
                value={String(pageSize)}
                onValueChange={(value) => onPageSizeChange(Number(value))}
              >
                <SelectTrigger className="h-9 w-[92px]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="25">25</SelectItem>
                  <SelectItem value="50">50</SelectItem>
                  <SelectItem value="100">100</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </div>
      </header>

      {sections.length === 0 ? (
        <section className="rounded-2xl border border-border/70 bg-card p-8 text-center text-sm text-muted-foreground">
          You don&apos;t have any task queues assigned to your roles.
        </section>
      ) : (
        sections.map((section) => (
          <BucketSection
            key={section.bucket}
            section={section}
            onLoadNext={onLoadNext}
          />
        ))
      )}
    </section>
  )
}

function approvalWorkbenchRouteForOutcome(outcome: string | null) {
  return outcome === 'ApprovedForPatching'
    ? '/workbenches/technical-manager/cases/$caseId'
    : '/workbenches/security-manager/cases/$caseId'
}

function BucketSection({
  section,
  onLoadNext,
}: {
  section: MyTaskBucket
  onLoadNext: (bucket: TaskBucketKey) => void
}) {
  const bucket = section.bucket
  const meta = BUCKET_LABELS[bucket]
  return (
    <section className="rounded-2xl border border-border/70 bg-card p-5">
      <div className="mb-4 flex items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <ClipboardList className="size-4 text-primary" />
            <h2 className="text-lg font-semibold">{meta.title}</h2>
            <span className="rounded-full border border-border/70 bg-muted/30 px-2 py-0.5 text-xs text-muted-foreground">
              {section.items.length.toLocaleString()} loaded
            </span>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">{meta.description}</p>
        </div>
      </div>

      <div className="overflow-x-auto rounded-xl border border-border/70">
        <table className="min-w-[860px] divide-y divide-border/70 text-sm">
          <thead className="bg-muted/30 text-left text-xs uppercase tracking-[0.14em] text-muted-foreground">
            <tr>
              <th className="px-4 py-3">Software</th>
              <th className="px-4 py-3">Task</th>
              <th className="px-4 py-3">Exposure</th>
              <th className="px-4 py-3">SLA</th>
              <th className="px-4 py-3">Owner routing</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody className="divide-y divide-border/60 bg-background">
            {section.items.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-sm text-muted-foreground">
                  Nothing waiting in this queue.
                </td>
              </tr>
            ) : (
              section.items.map((item) => (
                <tr key={item.remediationCaseId} className="align-top transition-colors hover:bg-muted/25">
                  <td className="px-4 py-3">
                    <div className="space-y-1">
                      <div className="font-medium">{startCase(item.softwareName)}</div>
                      <div className="flex flex-wrap gap-1.5">
                        <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(severityTone(item.criticality))}`}>
                          {item.criticality}
                        </span>
                        {item.riskBand ? (
                          <span className="text-[10px] text-muted-foreground">
                            {item.riskBand} risk
                          </span>
                        ) : null}
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <span className="inline-flex items-center gap-1.5 rounded-full border border-primary/25 bg-primary/10 px-2.5 py-1 text-xs font-medium">
                      <ShieldAlert className="size-3.5 text-primary" />
                      {meta.title}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    <div>{item.totalVulnerabilities.toLocaleString()} open vulnerabilities</div>
                    <div className="text-xs">{item.affectedDeviceCount.toLocaleString()} affected devices</div>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {item.slaDueDate ? (
                      <>
                        <div>{item.slaStatus ?? 'SLA tracked'}</div>
                        <div className="text-xs">Due {formatDate(item.slaDueDate)}</div>
                      </>
                    ) : (
                      'No due date'
                    )}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {formatSoftwareOwnerRoutingDetail(item.softwareOwnerTeamName, item.softwareOwnerAssignmentSource)}
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Link
                      to={
                        bucket === 'recommendation'
                          ? '/workbenches/security-analyst/cases/$caseId'
                          : bucket === 'approval'
                            ? approvalWorkbenchRouteForOutcome(item.outcome)
                            : '/workbenches/asset-owner/cases/$caseId'
                      }
                      params={{ caseId: item.remediationCaseId }}
                      className="inline-flex h-7 items-center justify-center rounded-lg bg-primary px-2.5 text-[0.8rem] font-medium text-primary-foreground transition-colors hover:bg-primary/80"
                    >
                      {meta.cta}
                    </Link>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      <div className="mt-4 flex items-center justify-between gap-3">
        <p className="text-sm text-muted-foreground">
          Page {section.page}
        </p>
        <Button
          variant="outline"
          size="sm"
          disabled={!section.hasMore}
          onClick={() => onLoadNext(bucket)}
        >
          {section.hasMore ? `Load next ${section.pageSize}` : 'All tasks loaded'}
        </Button>
      </div>
    </section>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-2xl font-semibold">{value}</p>
    </div>
  )
}
