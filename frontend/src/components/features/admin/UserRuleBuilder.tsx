import { Plus, Trash2, Layers } from 'lucide-react'
import type { FilterCondition, FilterGroup, FilterNode } from '@/api/asset-rules.schemas'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

type UserRuleBuilderProps = {
  value: FilterGroup
  onChange: (value: FilterGroup) => void
  readOnly?: boolean
}

const fields = [
  { value: 'DisplayName', label: 'Name' },
  { value: 'Email', label: 'Email / UPN' },
  { value: 'Company', label: 'Company' },
  { value: 'Role', label: 'Role' },
] as const

const operators = [
  { value: 'Equals', label: 'Equals' },
  { value: 'StartsWith', label: 'Starts with' },
  { value: 'Contains', label: 'Contains' },
  { value: 'EndsWith', label: 'Ends with' },
] as const

const roleOptions = [
  'GlobalAdmin',
  'SecurityManager',
  'SecurityAnalyst',
  'AssetOwner',
  'TechnicalManager',
  'Stakeholder',
  'Auditor',
] as const

export function UserRuleBuilder({ value, onChange, readOnly = false }: UserRuleBuilderProps) {
  return (
    <RuleGroupEditor
      group={value}
      onChange={onChange}
      isRoot
      readOnly={readOnly}
    />
  )
}

function RuleGroupEditor({
  group,
  onChange,
  onRemove,
  isRoot = false,
  readOnly = false,
}: {
  group: FilterGroup
  onChange: (value: FilterGroup) => void
  onRemove?: () => void
  isRoot?: boolean
  readOnly?: boolean
}) {
  const updateChild = (index: number, next: FilterNode) => {
    const updated = [...group.conditions]
    updated[index] = next
    onChange({ ...group, conditions: updated })
  }

  const removeChild = (index: number) => {
    onChange({ ...group, conditions: group.conditions.filter((_, childIndex) => childIndex !== index) })
  }

  const addCondition = () => {
    onChange({
      ...group,
      conditions: [
        ...group.conditions,
        { type: 'condition', field: 'Email', operator: 'Contains', value: '' },
      ],
    })
  }

  const addGroup = () => {
    onChange({
      ...group,
      conditions: [
        ...group.conditions,
        { type: 'group', operator: 'AND', conditions: [] },
      ],
    })
  }

  return (
    <div className={`rounded-xl border border-border/70 p-3 ${isRoot ? 'bg-card/50' : 'bg-muted/25'}`}>
      <div className="mb-3 flex items-center justify-between gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-7 rounded-lg px-3 text-xs font-semibold"
          onClick={() => onChange({ ...group, operator: group.operator === 'AND' ? 'OR' : 'AND' })}
          disabled={readOnly}
        >
          {group.operator}
        </Button>
        {!isRoot && onRemove ? (
          <Button type="button" variant="ghost" size="sm" className="h-7 text-destructive" onClick={onRemove} disabled={readOnly}>
            <Trash2 className="size-3.5" />
          </Button>
        ) : null}
      </div>

      <div className="space-y-2">
        {group.conditions.map((condition, index) =>
          condition.type === 'group' ? (
            <RuleGroupEditor
              key={index}
              group={condition}
              onChange={(next) => updateChild(index, next)}
              onRemove={() => removeChild(index)}
              readOnly={readOnly}
            />
          ) : (
            <RuleConditionEditor
              key={index}
              condition={condition}
              onChange={(next) => updateChild(index, next)}
              onRemove={() => removeChild(index)}
              readOnly={readOnly}
            />
          ),
        )}
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        <Button type="button" variant="ghost" size="sm" className="h-7 text-xs" onClick={addCondition} disabled={readOnly}>
          <Plus className="size-3" />
          Add condition
        </Button>
        <Button type="button" variant="ghost" size="sm" className="h-7 text-xs" onClick={addGroup} disabled={readOnly}>
          <Layers className="size-3" />
          Add group
        </Button>
      </div>
    </div>
  )
}

function RuleConditionEditor({
  condition,
  onChange,
  onRemove,
  readOnly = false,
}: {
  condition: FilterCondition
  onChange: (value: FilterCondition) => void
  onRemove: () => void
  readOnly?: boolean
}) {
  const isRole = condition.field === 'Role'

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Select
        value={condition.field}
        onValueChange={(field) => {
          if (!field) return
          onChange({
            ...condition,
            field,
            operator: field === 'Role' ? 'Equals' : condition.operator,
            value: field === 'Role' ? (condition.value || 'AssetOwner') : condition.value,
          })
        }}
        disabled={readOnly}
      >
        <SelectTrigger className="h-8 w-[160px] rounded-lg text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {fields.map((field) => (
            <SelectItem key={field.value} value={field.value}>
              {field.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select
        value={condition.operator}
        onValueChange={(operator) => operator && onChange({ ...condition, operator })}
        disabled={readOnly || isRole}
      >
        <SelectTrigger className="h-8 w-[140px] rounded-lg text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {operators.map((operator) => (
            <SelectItem key={operator.value} value={operator.value}>
              {operator.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      {isRole ? (
        <Select
          value={condition.value}
          onValueChange={(value) => value && onChange({ ...condition, value })}
          disabled={readOnly}
        >
          <SelectTrigger className="h-8 min-w-[180px] rounded-lg text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {roleOptions.map((role) => (
              <SelectItem key={role} value={role}>
                {role}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      ) : (
        <Input
          value={condition.value}
          onChange={(event) => onChange({ ...condition, value: event.target.value })}
          placeholder="Value"
          className="h-8 min-w-[180px] flex-1 rounded-lg text-xs"
          disabled={readOnly}
        />
      )}

      <Button type="button" variant="ghost" size="sm" className="h-8 text-destructive" onClick={onRemove} disabled={readOnly}>
        <Trash2 className="size-3.5" />
      </Button>
    </div>
  )
}
