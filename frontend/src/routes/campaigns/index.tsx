import { createFileRoute } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { CampaignList } from '@/components/features/campaigns/CampaignList'
import { CreateCampaignDialog } from '@/components/features/campaigns/CreateCampaignDialog'
import { useCampaigns, useCreateCampaign, type CampaignFilters } from '@/api/useCampaigns'

export const Route = createFileRoute('/campaigns/')({
  component: CampaignsPage,
})

function CampaignsPage() {
  const [status, setStatus] = useState('')
  const filters = useMemo<CampaignFilters>(() => ({ status: status || undefined, page: 1, pageSize: 50 }), [status])
  const campaignsQuery = useCampaigns(filters)
  const createMutation = useCreateCampaign()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Campaigns</h1>

      <div className="rounded-lg border border-border bg-card p-4">
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={status}
          onChange={(event) => {
            setStatus(event.target.value)
          }}
        >
          <option value="">All statuses</option>
          <option value="Active">Active</option>
          <option value="Closed">Closed</option>
        </select>
      </div>

      <CreateCampaignDialog
        isSubmitting={createMutation.isPending}
        onCreate={(name, description) => {
          createMutation.mutate({ name, description })
        }}
      />

      {campaignsQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading campaigns...</p> : null}
      {campaignsQuery.isError ? <p className="text-sm text-destructive">Failed to load campaigns.</p> : null}

      {campaignsQuery.data ? <CampaignList items={campaignsQuery.data.items} totalCount={campaignsQuery.data.totalCount} /> : null}
    </section>
  )
}
