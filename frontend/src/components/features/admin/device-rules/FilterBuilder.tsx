import { Plus, Trash2, Layers } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { FilterCondition, FilterGroup, FilterNode } from '@/api/device-rules.schemas'

// Phase 1 canonical cleanup (Task 15): device-only filter builder.
// The legacy AssetType / Software / CloudResource branch has been
// removed alongside the AssetType abstraction — rules now target
// devices exclusively.

type FilterBuilderProps = {
  value: FilterGroup
  onChange: (value: FilterGroup) => void
}

const allFields = [
  { value: 'Name', label: 'Name' },
  { value: 'DeviceGroup', label: 'Device Group' },
  { value: 'Platform', label: 'Platform' },
  { value: 'Domain', label: 'Domain' },
  { value: 'Tag', label: 'Tag' },
] as const

const operators = [
  { value: 'Equals', label: 'Equals' },
  { value: 'StartsWith', label: 'Starts with' },
  { value: 'Contains', label: 'Contains' },
  { value: 'EndsWith', label: 'Ends with' },
] as const

export function FilterBuilder({ value, onChange }: FilterBuilderProps) {
  return (
    <FilterGroupEditor
      group={value}
      onChange={onChange}
      isRoot
    />
  )
}

function FilterGroupEditor({
  group,
  onChange,
  onRemove,
  isRoot = false,
}: {
  group: FilterGroup
  onChange: (group: FilterGroup) => void
  onRemove?: () => void
  isRoot?: boolean
}) {
  const toggleOperator = () => {
    onChange({ ...group, operator: group.operator === 'AND' ? 'OR' : 'AND' })
  }

  const addCondition = () => {
    const newCondition: FilterCondition = {
      type: 'condition',
      field: allFields[0].value,
      operator: 'Equals',
      value: '',
    }
    onChange({ ...group, conditions: [...group.conditions, newCondition] })
  }

  const addGroup = () => {
    const newGroup: FilterGroup = {
      type: 'group',
      operator: 'AND',
      conditions: [],
    }
    onChange({ ...group, conditions: [...group.conditions, newGroup] })
  }

  const updateChild = (index: number, updated: FilterNode) => {
    const next = [...group.conditions]
    next[index] = updated
    onChange({ ...group, conditions: next })
  }

  const removeChild = (index: number) => {
    onChange({ ...group, conditions: group.conditions.filter((_, i) => i !== index) })
  }

  return (
    <div className={`rounded-xl border border-border/70 ${isRoot ? 'bg-card/50' : 'bg-muted/30'} p-3`}>
      <div className="mb-2 flex items-center justify-between gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-7 rounded-lg px-3 text-xs font-semibold"
          onClick={toggleOperator}
        >
          {group.operator}
        </Button>
        {!isRoot && onRemove && (
          <Tooltip>
            <TooltipTrigger
              render={
                <Button type="button" variant="ghost" size="sm" className="h-7 text-destructive" onClick={onRemove} />
              }
            >
              <Trash2 className="size-3.5" />
            </TooltipTrigger>
            <TooltipContent>Delete group</TooltipContent>
          </Tooltip>
        )}
      </div>

      <div className="space-y-2">
        {group.conditions.map((child, index) =>
          child.type === 'group' ? (
            <FilterGroupEditor
              key={index}
              group={child}
              onChange={(updated) => updateChild(index, updated)}
              onRemove={() => removeChild(index)}
            />
          ) : (
            <FilterConditionEditor
              key={index}
              condition={child}
              onChange={(updated) => updateChild(index, updated)}
              onRemove={() => removeChild(index)}
            />
          ),
        )}
      </div>

      <div className="mt-2 flex items-center gap-2">
        <Button type="button" variant="ghost" size="sm" className="h-7 text-xs" onClick={addCondition}>
          <Plus className="size-3" />
          Add condition
        </Button>
        <Button type="button" variant="ghost" size="sm" className="h-7 text-xs" onClick={addGroup}>
          <Layers className="size-3" />
          Add group
        </Button>
      </div>
    </div>
  )
}

function FilterConditionEditor({
  condition,
  onChange,
  onRemove,
}: {
  condition: FilterCondition
  onChange: (condition: FilterCondition) => void
  onRemove: () => void
}) {
  return (
    <div className="flex items-center gap-2">
      <Select
        value={condition.field}
        onValueChange={(field) => {
          if (!field) return
          onChange({ ...condition, field })
        }}
      >
        <SelectTrigger className="h-8 w-[140px] rounded-lg text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {allFields.map((f) => (
            <SelectItem key={f.value} value={f.value}>
              {f.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select
        value={condition.operator}
        onValueChange={(operator) => operator && onChange({ ...condition, operator })}
      >
        <SelectTrigger className="h-8 w-[130px] rounded-lg text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {operators.map((op) => (
            <SelectItem key={op.value} value={op.value}>
              {op.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Input
        value={condition.value}
        onChange={(e) => onChange({ ...condition, value: e.target.value })}
        placeholder="Value..."
        className="h-8 min-w-[150px] flex-1 rounded-lg text-xs"
      />

      <Tooltip>
        <TooltipTrigger
          render={
            <Button type="button" variant="ghost" size="sm" className="h-7 shrink-0 text-destructive" onClick={onRemove} />
          }
        >
          <Trash2 className="size-3.5" />
        </TooltipTrigger>
        <TooltipContent>Delete condition</TooltipContent>
      </Tooltip>
    </div>
  )
}
