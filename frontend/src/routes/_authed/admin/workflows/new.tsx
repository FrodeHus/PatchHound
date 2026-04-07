import { useState } from 'react'
import { createFileRoute, redirect, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { toast } from 'sonner'
import { createWorkflowDefinition } from '@/api/workflows.functions'
import { fetchTeams } from '@/api/teams.functions'
import { WorkflowDesigner, type WorkflowGraph } from '@/components/features/workflows/WorkflowDesigner'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'

export const Route = createFileRoute('/_authed/admin/workflows/new')({
  beforeLoad: ({ context }) => {
    if (!context.user?.featureFlags.workflows) {
      throw redirect({ to: '/admin' })
    }
  },
  loader: async () => {
    const teams = await fetchTeams({ data: { pageSize: 200 } })
    return { teams }
  },
  component: NewWorkflowPage,
})

function NewWorkflowPage() {
  const router = useRouter()
  const { teams } = Route.useLoaderData()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [scope, setScope] = useState('Tenant')
  const [triggerType, setTriggerType] = useState('VulnerabilityDetected')

  const createMutation = useMutation({
    mutationFn: async (graph: WorkflowGraph) => {
      return createWorkflowDefinition({
        data: {
          name,
          description: description || null,
          scope,
          triggerType,
          graphJson: JSON.stringify(graph),
        },
      })
    },
    onSuccess: (result) => {
      toast.success('Workflow created')
      void router.navigate({ to: '/admin/workflows/$id', params: { id: result.id } })
    },
    onError: () => {
      toast.error('Failed to create workflow')
    },
  })

  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="space-y-2">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Workflows</p>
          <h1 className="text-3xl font-semibold tracking-[-0.04em]">New Workflow</h1>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-4">
        <div className="space-y-3">
          <div>
            <label className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Name
            </label>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Critical Vuln Triage"
            />
          </div>
          <div>
            <label className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Description
            </label>
            <Textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Optional description..."
              rows={3}
            />
          </div>
          <div>
            <label className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Scope
            </label>
            <Select value={scope} onValueChange={(v) => v && setScope(v)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Tenant">Tenant</SelectItem>
                <SelectItem value="System">System</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div>
            <label className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Trigger
            </label>
            <Select value={triggerType} onValueChange={(v) => v && setTriggerType(v)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="VulnerabilityDetected">Vulnerability Detected</SelectItem>
                <SelectItem value="VulnerabilityReopened">Vulnerability Reopened</SelectItem>
                <SelectItem value="AssetOnboarded">Asset Onboarded</SelectItem>
                <SelectItem value="ScheduledIngestion">Scheduled Ingestion</SelectItem>
                <SelectItem value="ManualRun">Manual Run</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <div className="md:col-span-3">
          <WorkflowDesigner
            teams={teams.items}
            onSave={(graph) => {
              if (!name.trim()) {
                toast.error('Please enter a workflow name')
                return
              }
              createMutation.mutate(graph)
            }}
          />
        </div>
      </div>
    </section>
  )
}
