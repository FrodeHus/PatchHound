import { useState } from 'react'
import type { CampaignDetail as CampaignDetailModel } from '@/api/useCampaigns'

type CampaignDetailProps = {
  campaign: CampaignDetailModel
  isLinking: boolean
  isAssigning: boolean
  onLinkVulnerabilities: (ids: string[]) => void
  onBulkAssign: (assigneeId: string) => void
}

export function CampaignDetail({
  campaign,
  isLinking,
  isAssigning,
  onLinkVulnerabilities,
  onBulkAssign,
}: CampaignDetailProps) {
  const [vulnerabilityIdsInput, setVulnerabilityIdsInput] = useState('')
  const [assigneeId, setAssigneeId] = useState('')

  return (
    <section className="space-y-4">
      <header className="rounded-lg border border-border bg-card p-4">
        <h1 className="text-2xl font-semibold">{campaign.name}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{campaign.description ?? 'No description'}</p>
        <p className="mt-2 text-xs text-muted-foreground">
          Status: {campaign.status} | {campaign.completedTasks}/{campaign.totalTasks} tasks complete
        </p>
      </header>

      <section className="rounded-lg border border-border bg-card p-4">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Linked Vulnerabilities</h3>
        <p className="mt-1 text-xs text-muted-foreground">{campaign.vulnerabilityCount} vulnerabilities linked</p>
        <div className="mt-2 flex flex-wrap gap-2">
          {campaign.vulnerabilityIds.map((id) => (
            <code key={id} className="rounded bg-muted px-2 py-1 text-xs">{id}</code>
          ))}
        </div>

        <div className="mt-3 grid gap-2 md:grid-cols-[1fr_auto]">
          <input
            className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
            placeholder="Comma-separated vulnerability GUIDs"
            value={vulnerabilityIdsInput}
            onChange={(event) => {
              setVulnerabilityIdsInput(event.target.value)
            }}
          />
          <button
            type="button"
            className="rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted disabled:opacity-50"
            disabled={isLinking || vulnerabilityIdsInput.trim().length === 0}
            onClick={() => {
              const ids = vulnerabilityIdsInput
                .split(',')
                .map((id) => id.trim())
                .filter((id) => id.length > 0)
              onLinkVulnerabilities(ids)
              setVulnerabilityIdsInput('')
            }}
          >
            {isLinking ? 'Linking...' : 'Link vulnerabilities'}
          </button>
        </div>
      </section>

      <section className="rounded-lg border border-border bg-card p-4">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Bulk Assign</h3>
        <div className="mt-2 grid gap-2 md:grid-cols-[1fr_auto]">
          <input
            className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
            placeholder="Assignee GUID"
            value={assigneeId}
            onChange={(event) => {
              setAssigneeId(event.target.value)
            }}
          />
          <button
            type="button"
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
            disabled={isAssigning || assigneeId.trim().length === 0}
            onClick={() => {
              onBulkAssign(assigneeId.trim())
              setAssigneeId('')
            }}
          >
            {isAssigning ? 'Assigning...' : 'Bulk assign tasks'}
          </button>
        </div>
      </section>
    </section>
  )
}
