import { Plus, Trash2, Layers } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { FilterCondition, FilterGroup, FilterNode } from '@/api/asset-rules.schemas'

type FilterBuilderProps = {
  value: FilterGroup
  onChange: (value: FilterGroup) => void
}

const allFields = [
  { value: 'AssetType', label: 'Asset Type' },
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

const assetTypes = [
  { value: 'Device', label: 'Device' },
  { value: 'Software', label: 'Software' },
  { value: 'CloudResource', label: 'Cloud Resource' },
] as const

function getConstrainedAssetType(group: FilterGroup): string | null {
  for (const child of group.conditions) {
    if (child.type === 'condition') {
      const cond = child as FilterCondition
      if (cond.field === 'AssetType' && cond.operator === 'Equals') return cond.value
    }
  }
  return null
}

function getAvailableFields(ancestors: FilterGroup[]): typeof allFields[number][] {
  let assetType: string | null = null
  for (const group of ancestors) {
    assetType = getConstrainedAssetType(group) ?? assetType
  }

  return allFields.filter((f) => {
    if (['DeviceGroup', 'Platform', 'Domain'].includes(f.value)) return assetType === 'Device'
    return true
  })
}

export function FilterBuilder({ value, onChange }: FilterBuilderProps) {
  return (
    <FilterGroupEditor
      group={value}
      onChange={onChange}
      ancestors={[]}
      isRoot
    />
  )
}

function FilterGroupEditor({
  group,
  onChange,
  onRemove,
  ancestors,
  isRoot = false,
}: {
  group: FilterGroup
  onChange: (group: FilterGroup) => void
  onRemove?: () => void
  ancestors: FilterGroup[]
  isRoot?: boolean
}) {
  const currentAncestors = [...ancestors, group]

  const toggleOperator = () => {
    onChange({ ...group, operator: group.operator === 'AND' ? 'OR' : 'AND' })
  }

  const addCondition = () => {
    const fields = getAvailableFields(currentAncestors)
    const defaultField = fields[0]?.value ?? 'Name'
    const newCondition: FilterCondition = {
      type: 'condition',
      field: defaultField,
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
              group={child as FilterGroup}
              onChange={(updated) => updateChild(index, updated)}
              onRemove={() => removeChild(index)}
              ancestors={currentAncestors}
            />
          ) : (
            <FilterConditionEditor
              key={index}
              condition={child as FilterCondition}
              onChange={(updated) => updateChild(index, updated)}
              onRemove={() => removeChild(index)}
              ancestors={currentAncestors}
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
  ancestors,
}: {
  condition: FilterCondition
  onChange: (condition: FilterCondition) => void
  onRemove: () => void
  ancestors: FilterGroup[]
}) {
  const availableFields = getAvailableFields(ancestors)
  const isAssetType = condition.field === 'AssetType'

  return (
    <div className="flex items-center gap-2">
      <Select
        value={condition.field}
        onValueChange={(field) => {
          if (!field) return
          const updates: Partial<FilterCondition> = { field }
          if (field === 'AssetType') {
            updates.operator = 'Equals'
            updates.value = 'Device'
          } else if (condition.field === 'AssetType') {
            updates.value = ''
          }
          onChange({ ...condition, ...updates })
        }}
      >
        <SelectTrigger className="h-8 w-[140px] rounded-lg text-xs">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {availableFields.map((f) => (
            <SelectItem key={f.value} value={f.value}>
              {f.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>

      {!isAssetType && (
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
      )}

      {isAssetType ? (
        <Select
          value={condition.value}
          onValueChange={(value) => value && onChange({ ...condition, value })}
        >
          <SelectTrigger className="h-8 w-[150px] rounded-lg text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {assetTypes.map((at) => (
              <SelectItem key={at.value} value={at.value}>
                {at.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      ) : (
        <Input
          value={condition.value}
          onChange={(e) => onChange({ ...condition, value: e.target.value })}
          placeholder="Value..."
          className="h-8 min-w-[150px] flex-1 rounded-lg text-xs"
        />
      )}

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
