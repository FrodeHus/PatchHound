import { useCallback } from 'react'
import type { Node } from '@xyflow/react'
import { X, Plus, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { TeamItem } from '@/api/teams.schemas'

type ConditionRule = {
  field: string
  operator: string
  value: string
}

type NodeConfigPanelProps = {
  node: Node
  teams: TeamItem[]
  onUpdate: (nodeId: string, data: Record<string, unknown>) => void
  onClose: () => void
}

const conditionOperators = [
  { value: 'eq', label: '=' },
  { value: 'neq', label: '≠' },
  { value: 'gt', label: '>' },
  { value: 'gte', label: '≥' },
  { value: 'lt', label: '<' },
  { value: 'lte', label: '≤' },
  { value: 'contains', label: 'contains' },
]

const conditionFields = [
  { value: 'asset.criticality', label: 'Asset Criticality' },
  { value: 'vulnerability.severity', label: 'Vulnerability Severity' },
  { value: 'vulnerability.cvssScore', label: 'CVSS Score' },
  { value: 'vulnerability.exploitAvailable', label: 'Exploit Available' },
  { value: 'asset.type', label: 'Asset Type' },
  { value: 'asset.deviceValue', label: 'Device Value' },
]

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <label className="text-xs font-medium text-muted-foreground">{label}</label>
      {children}
    </div>
  )
}

export function NodeConfigPanel({ node, teams, onUpdate, onClose }: NodeConfigPanelProps) {
  const data = node.data

  const update = useCallback(
    (key: string, value: unknown) => {
      onUpdate(node.id, { ...data, [key]: value })
    },
    [node.id, data, onUpdate],
  )

  const renderConfig = () => {
    switch (node.type) {
      case 'AssignGroup':
        return <AssignGroupConfig data={data} teams={teams} update={update} />
      case 'WaitForAction':
        return <WaitForActionConfig data={data} update={update} />
      case 'SendNotification':
        return <SendNotificationConfig data={data} teams={teams} update={update} />
      case 'Condition':
        return <ConditionConfig data={data} update={update} />
      case 'SystemTask':
        return <SystemTaskConfig data={data} update={update} />
      case 'Merge':
        return <MergeConfig data={data} update={update} />
      default:
        return <p className="text-xs text-muted-foreground">This node has no configurable properties.</p>
    }
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between border-b border-border/60 px-4 py-3">
        <h3 className="text-sm font-semibold">
          {node.type?.replace(/([A-Z])/g, ' $1').trim()}
        </h3>
        <Button variant="ghost" size="sm" className="size-7 p-0" onClick={onClose}>
          <X className="size-4" />
        </Button>
      </div>
      <div className="flex-1 space-y-4 overflow-y-auto p-4">
        <Field label="Label">
          <Input
            value={(data.label as string) ?? ''}
            onChange={(e) => update('label', e.target.value)}
            placeholder="Node label"
          />
        </Field>
        {renderConfig()}
      </div>
    </div>
  )
}

function AssignGroupConfig({
  data,
  teams,
  update,
}: {
  data: Record<string, unknown>
  teams: TeamItem[]
  update: (key: string, value: unknown) => void
}) {
  return (
    <>
      <Field label="Team">
        <Select
          value={(data.teamId as string) ?? ''}
          onValueChange={(v) => v && update('teamId', v)}
        >
          <SelectTrigger>
            <SelectValue placeholder="Select team…" />
          </SelectTrigger>
          <SelectContent>
            {teams.map((team) => (
              <SelectItem key={team.id} value={team.id}>
                {team.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>
      <Field label="Required Action">
        <Select
          value={(data.requiredAction as string) ?? 'Review'}
          onValueChange={(v) => v && update('requiredAction', v)}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="Review">Review</SelectItem>
            <SelectItem value="FillForm">Fill Form</SelectItem>
            <SelectItem value="QA">QA</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field label="Instructions">
        <Textarea
          value={(data.instructions as string) ?? ''}
          onChange={(e) => update('instructions', e.target.value)}
          placeholder="Instructions for the team…"
          rows={3}
        />
      </Field>
      <Field label="Timeout (hours)">
        <Input
          type="number"
          min={0}
          value={(data.timeoutHours as number) ?? ''}
          onChange={(e) => update('timeoutHours', e.target.value ? Number(e.target.value) : undefined)}
          placeholder="e.g. 48"
        />
      </Field>
    </>
  )
}

function WaitForActionConfig({
  data,
  update,
}: {
  data: Record<string, unknown>
  update: (key: string, value: unknown) => void
}) {
  return (
    <Field label="Timeout (hours)">
      <Input
        type="number"
        min={0}
        value={(data.timeoutHours as number) ?? ''}
        onChange={(e) => update('timeoutHours', e.target.value ? Number(e.target.value) : undefined)}
        placeholder="e.g. 48"
      />
    </Field>
  )
}

function SendNotificationConfig({
  data,
  teams,
  update,
}: {
  data: Record<string, unknown>
  teams: TeamItem[]
  update: (key: string, value: unknown) => void
}) {
  return (
    <>
      <Field label="Team">
        <Select
          value={(data.teamId as string) ?? ''}
          onValueChange={(v) => v && update('teamId', v)}
        >
          <SelectTrigger>
            <SelectValue placeholder="Select team…" />
          </SelectTrigger>
          <SelectContent>
            {teams.map((team) => (
              <SelectItem key={team.id} value={team.id}>
                {team.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>
      <Field label="Channel">
        <Select
          value={(data.channel as string) ?? 'Email'}
          onValueChange={(v) => v && update('channel', v)}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="Email">Email</SelectItem>
            <SelectItem value="InApp">In-App</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field label="Template Key">
        <Input
          value={(data.templateKey as string) ?? ''}
          onChange={(e) => update('templateKey', e.target.value)}
          placeholder="e.g. vulnerability-alert"
        />
      </Field>
    </>
  )
}

function ConditionConfig({
  data,
  update,
}: {
  data: Record<string, unknown>
  update: (key: string, value: unknown) => void
}) {
  const rules = (data.rules as ConditionRule[] | undefined) ?? []

  const updateRule = (index: number, field: keyof ConditionRule, value: string) => {
    const updated = rules.map((r, i) => (i === index ? { ...r, [field]: value } : r))
    update('rules', updated)
  }

  const addRule = () => {
    update('rules', [...rules, { field: '', operator: 'eq', value: '' }])
  }

  const removeRule = (index: number) => {
    update('rules', rules.filter((_, i) => i !== index))
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-muted-foreground">Conditions</span>
        <Button variant="ghost" size="sm" className="h-6 gap-1 px-2 text-xs" onClick={addRule}>
          <Plus className="size-3" /> Add Rule
        </Button>
      </div>
      {rules.length === 0 && (
        <p className="text-xs text-muted-foreground/70">No rules defined. Add a rule to configure branching.</p>
      )}
      {rules.map((rule, i) => (
        <div key={i} className="space-y-2 rounded-lg border border-border/50 bg-card/40 p-2.5">
          <div className="flex items-center justify-between">
            <span className="text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
              Rule {i + 1}
            </span>
            <Button
              variant="ghost"
              size="sm"
              className="size-5 p-0 text-muted-foreground hover:text-destructive"
              onClick={() => removeRule(i)}
            >
              <Trash2 className="size-3" />
            </Button>
          </div>
          <Select value={rule.field} onValueChange={(v) => v && updateRule(i, 'field', v)}>
            <SelectTrigger>
              <SelectValue placeholder="Select field…" />
            </SelectTrigger>
            <SelectContent>
              {conditionFields.map((f) => (
                <SelectItem key={f.value} value={f.value}>
                  {f.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="flex gap-2">
            <Select value={rule.operator} onValueChange={(v) => v && updateRule(i, 'operator', v)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {conditionOperators.map((op) => (
                  <SelectItem key={op.value} value={op.value}>
                    {op.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Input
              value={rule.value}
              onChange={(e) => updateRule(i, 'value', e.target.value)}
              placeholder="Value"
              className="flex-1"
            />
          </div>
        </div>
      ))}
    </div>
  )
}

function SystemTaskConfig({
  data,
  update,
}: {
  data: Record<string, unknown>
  update: (key: string, value: unknown) => void
}) {
  return (
    <>
      <Field label="Task Type">
        <Select
          value={(data.taskType as string) ?? ''}
          onValueChange={(v) => v && update('taskType', v)}
        >
          <SelectTrigger>
            <SelectValue placeholder="Select task type…" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="RunIngestion">Run Ingestion</SelectItem>
            <SelectItem value="Enrich">Enrich</SelectItem>
            <SelectItem value="RecalculateScore">Recalculate Score</SelectItem>
            <SelectItem value="ApplyRule">Apply Rule</SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field label="Configuration (JSON)">
        <Textarea
          value={(data.config as string) ?? ''}
          onChange={(e) => update('config', e.target.value)}
          placeholder='{"key": "value"}'
          rows={4}
          className="font-mono text-xs"
        />
      </Field>
    </>
  )
}

function MergeConfig({
  data,
  update,
}: {
  data: Record<string, unknown>
  update: (key: string, value: unknown) => void
}) {
  return (
    <Field label="Wait Mode">
      <Select
        value={(data.waitMode as string) ?? 'all'}
        onValueChange={(v) => v && update('waitMode', v)}
      >
        <SelectTrigger>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Wait for All</SelectItem>
          <SelectItem value="any">Wait for Any</SelectItem>
        </SelectContent>
      </Select>
    </Field>
  )
}
