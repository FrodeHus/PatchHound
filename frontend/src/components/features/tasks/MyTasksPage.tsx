import { Link } from '@tanstack/react-router'
import { ClipboardList, ShieldAlert } from 'lucide-react'
import type { PagedDecisionList } from '@/api/remediation.schemas'
import { Button } from '@/components/ui/button'
import { formatDate, startCase } from '@/lib/formatting'
import { toneBadge } from '@/lib/tone-classes'
import {
  formatSoftwareOwnerRoutingDetail,
  severityTone,
} from '@/components/features/remediation/remediation-utils'
import { BUCKET_LABELS, type TaskBucketKey } from './my-tasks-buckets'

type Section = { bucket: TaskBucketKey; data: PagedDecisionList }

type MyTasksPageProps = {
  sections: Section[]
  onPageChange: (page: number) => void
}

export function MyTasksPage({ sections, onPageChange }: MyTasksPageProps) {
  const totalCount = sections.reduce((sum, section) => sum + section.data.totalCount, 0)
  const visibleCount = sections.reduce((sum, section) => sum + section.data.items.length, 0)

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
          <div className="grid min-w-[220px] gap-3 rounded-xl border border-border/70 bg-background/50 p-4">
            <Metric label="Open across all queues" value={totalCount.toLocaleString()} />
            <Metric label="Visible on page" value={visibleCount.toLocaleString()} />
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
            bucket={section.bucket}
            data={section.data}
            onPageChange={onPageChange}
          />
        ))
      )}
    </section>
  )
}

function BucketSection({
  bucket,
  data,
  onPageChange,
}: {
  bucket: TaskBucketKey
  data: PagedDecisionList
  onPageChange: (page: number) => void
}) {
  const meta = BUCKET_LABELS[bucket]
  return (
    <section className="rounded-2xl border border-border/70 bg-card p-5">
      <div className="mb-4 flex items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <ClipboardList className="size-4 text-primary" />
            <h2 className="text-lg font-semibold">{meta.title}</h2>
            <span className="rounded-full border border-border/70 bg-muted/30 px-2 py-0.5 text-xs text-muted-foreground">
              {data.totalCount.toLocaleString()}
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
            {data.items.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-sm text-muted-foreground">
                  Nothing waiting in this queue.
                </td>
              </tr>
            ) : (
              data.items.map((item) => (
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
                    {bucket === 'recommendation' ? (
                      <Link
                        to="/workbenches/security-analyst/cases/$caseId"
                        params={{ caseId: item.remediationCaseId }}
                        className="inline-flex h-7 items-center justify-center rounded-lg bg-primary px-2.5 text-[0.8rem] font-medium text-primary-foreground transition-colors hover:bg-primary/80"
                      >
                        {meta.cta}
                      </Link>
                    ) : (
                      <Link
                        to="/workbenches/asset-owner/cases/$caseId"
                        params={{ caseId: item.remediationCaseId }}
                        className="inline-flex h-7 items-center justify-center rounded-lg bg-primary px-2.5 text-[0.8rem] font-medium text-primary-foreground transition-colors hover:bg-primary/80"
                      >
                        {meta.cta}
                      </Link>
                    )}
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
            size="sm"
            disabled={data.page <= 1}
            onClick={() => onPageChange(data.page - 1)}
          >
            Previous
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={data.page >= data.totalPages}
            onClick={() => onPageChange(data.page + 1)}
          >
            Next
          </Button>
        </div>
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
