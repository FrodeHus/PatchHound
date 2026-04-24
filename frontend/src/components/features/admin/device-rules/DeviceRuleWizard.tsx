import { useMutation } from '@tanstack/react-query'
import { useRouter } from '@tanstack/react-router'
import { toast } from 'sonner'
import { ArrowLeft, ArrowRight, Check, Eye, Loader2 } from 'lucide-react'
import { useState } from 'react'
import {
  createDeviceRule,
  previewDeviceRuleFilter,
  updateDeviceRule,
} from '@/api/device-rules.functions'
import {
  assetRuleAssetTypeSchema,
  type AssetRuleAssetType,
} from '@/api/device-rules.schemas'
import type {
  DeviceRule,
  DeviceRuleOperation,
  FilterGroup,
  FilterPreview,
} from '@/api/device-rules.schemas'
import type { ScanProfile } from '@/api/authenticated-scans.schemas'
import type { BusinessLabel } from '@/api/business-labels.schemas'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import type { TeamItem } from '@/api/teams.schemas'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { Badge } from '@/components/ui/badge'
import { getApiErrorMessage } from '@/lib/api-errors'
import { FilterBuilder } from './FilterBuilder'

type DeviceRuleWizardProps = {
  mode: 'create' | 'edit'
  tenantId?: string
  initialData?: DeviceRule
  securityProfiles: SecurityProfile[]
  businessLabels: BusinessLabel[]
  teams: TeamItem[]
  scanProfiles: ScanProfile[]
  onCancel?: () => void
  onSaved?: (rule: DeviceRule) => void | Promise<void>
}

const steps = ['Basic Info', 'Filters', 'Operations', 'Summary'] as const
const criticalityOptions = [
  { value: 'Critical', label: 'Critical' },
  { value: 'High', label: 'High' },
  { value: 'Medium', label: 'Medium' },
  { value: 'Low', label: 'Low' },
]

const emptyFilter: FilterGroup = { type: 'group', operator: 'AND', conditions: [] }

export function DeviceRuleWizard({
  mode,
  tenantId,
  initialData,
  securityProfiles,
  businessLabels,
  teams,
  scanProfiles,
  onCancel,
  onSaved,
}: DeviceRuleWizardProps) {
  const router = useRouter()
  const [step, setStep] = useState(0)
  const [assetType, setAssetType] = useState<AssetRuleAssetType>(
    assetRuleAssetTypeSchema.parse(initialData?.assetType ?? 'Device'),
  )
  const [name, setName] = useState(initialData?.name ?? '')
  const [description, setDescription] = useState(initialData?.description ?? '')
  const [filter, setFilter] = useState<FilterGroup>(
    initialData?.filterDefinition?.type === 'group'
      ? initialData.filterDefinition
      : emptyFilter,
  )
  const [operations, setOperations] = useState<DeviceRuleOperation[]>(
    initialData?.operations ?? [],
  )
  const [preview, setPreview] = useState<FilterPreview | null>(null)
  const isSoftwareRule = assetType === 'Software'
  const isApplicationRule = assetType === 'Application'
  const supportsOwnerOnlyRule = isSoftwareRule || isApplicationRule

  const previewMutation = useMutation({
    mutationFn: async () => previewDeviceRuleFilter({ data: { tenantId, assetType, filterDefinition: filter } }),
    onSuccess: (data) => setPreview(data),
  })

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (mode === 'edit' && initialData) {
        return updateDeviceRule({
          data: {
            tenantId,
            assetType,
            id: initialData.id,
            name,
            description: description || undefined,
            enabled: initialData.enabled,
            filterDefinition: filter,
            operations,
          },
        })
      }
      return createDeviceRule({
        data: {
          tenantId,
          assetType,
          name,
          description: description || undefined,
          filterDefinition: filter,
          operations,
        },
      })
    },
    onSuccess: async (rule) => {
      toast.success(mode === 'create' ? 'Rule created' : 'Changes saved')
      if (onSaved) {
        await onSaved(rule)
      } else {
        await router.navigate({ to: '/admin/device-rules', search: { page: 1, pageSize: 25 } })
      }
      await router.invalidate()
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, mode === 'create' ? 'Failed to create rule' : 'Failed to save changes'))
    },
  })

  const canContinue = () => {
    switch (step) {
      case 0:
        return name.trim().length > 0
      case 1:
        return filter.conditions.length > 0
      case 2:
        return supportsOwnerOnlyRule || operations.length > 0
      default:
        return true
    }
  }

  const handleAssetTypeChange = (value: string | null) => {
    if (!value) return
    const nextAssetType = assetRuleAssetTypeSchema.parse(value)
    setAssetType(nextAssetType)
    setFilter(emptyFilter)
    setOperations([])
    setPreview(null)
  }

  return (
    <section className="mx-auto max-w-3xl space-y-5">
      {/* Step indicator */}
      <div className="flex items-center gap-2">
        {steps.map((label, i) => (
          <button
            key={label}
            type="button"
            className={`flex items-center gap-2 rounded-lg border px-3 py-1.5 text-xs font-medium transition ${
              i === step
                ? "border-primary/40 bg-primary/10 text-primary"
                : i < step
                  ? "border-border/50 bg-muted/50 text-muted-foreground"
                  : "border-border/30 text-muted-foreground/50"
            }`}
            onClick={() => i < step && setStep(i)}
            disabled={i > step}
          >
            <span className="flex size-5 items-center justify-center rounded-full border border-current text-[10px] font-bold">
              {i < step ? <Check className="size-3" /> : i + 1}
            </span>
            {label}
          </button>
        ))}
      </div>

      {/* Step content */}
      {step === 0 && (
        <Card className="rounded-2xl border-border/70">
          <CardHeader>
            <CardTitle>Basic Information</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-1.5">
              <label className="text-xs font-medium text-muted-foreground">
                Asset type
              </label>
              <Select value={assetType} onValueChange={handleAssetTypeChange}>
                <SelectTrigger className="rounded-lg">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Device">Device</SelectItem>
                  <SelectItem value="Software">Software</SelectItem>
                  <SelectItem value="Application">Application</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                Device, software, and application rules support owner-team assignment. Device-only operations remain available where applicable.
              </p>
            </div>
            <div className="grid gap-1.5">
              <label className="text-xs font-medium text-muted-foreground">
                Rule name
              </label>
              <Input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. Production servers — high security"
                className="rounded-lg"
              />
            </div>
            <div className="grid gap-1.5">
              <label className="text-xs font-medium text-muted-foreground">
                Description (optional)
              </label>
              <Textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="What does this rule do?"
                rows={3}
                className="rounded-lg"
              />
            </div>
          </CardContent>
        </Card>
      )}

      {step === 1 && (
        <Card className="rounded-2xl border-border/70">
          <CardHeader>
            <CardTitle>Filter Conditions</CardTitle>
            <p className="text-sm text-muted-foreground">
              {isSoftwareRule
                ? 'Define which software assets this rule applies to. Use groups to combine conditions with AND/OR logic.'
                : isApplicationRule
                  ? 'Define which cloud applications this rule applies to. Use groups to combine conditions with AND/OR logic.'
                : 'Define which devices this rule applies to. Use groups to combine conditions with AND/OR logic.'}
            </p>
          </CardHeader>
          <CardContent className="space-y-4">
            <FilterBuilder assetType={assetType} value={filter} onChange={setFilter} />

            {filter.conditions.length > 0 && (
              <div className="space-y-3">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => previewMutation.mutate()}
                  disabled={previewMutation.isPending}
                >
                  {previewMutation.isPending ? (
                    <Loader2 className="size-3.5 animate-spin" />
                  ) : (
                    <Eye className="size-3.5" />
                  )}
                  Preview matches
                </Button>

                {preview && (
                  <InsetPanel className="space-y-2 px-4 py-3">
                    <p className="text-sm font-medium">
                      {buildPreviewHeadline(assetType, preview.count, operations, securityProfiles, businessLabels, teams, scanProfiles)}
                    </p>
                    {operations.length > 0 ? (
                      <div className="flex flex-wrap gap-2">
                        {buildOperationImpactLines(operations, securityProfiles, businessLabels, teams, scanProfiles).map((line) => (
                          <Badge key={line} variant="outline" className="rounded-full bg-background/80">
                            {line}
                          </Badge>
                        ))}
                      </div>
                    ) : null}
                    {preview.samples.length > 0 && (
                      <div className="space-y-1">
                        {preview.samples.map((s) => (
                          <div
                            key={s.id}
                            className="flex items-center gap-2 text-xs"
                          >
                            <span>{s.name}</span>
                          </div>
                        ))}
                        {preview.count > preview.samples.length && (
                          <p className="text-xs text-muted-foreground">
                            and {preview.count - preview.samples.length} more...
                          </p>
                        )}
                      </div>
                    )}
                  </InsetPanel>
                )}
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {step === 2 && (
        <Card className="rounded-2xl border-border/70">
          <CardHeader>
              <CardTitle>Operations</CardTitle>
              <p className="text-sm text-muted-foreground">
              {isSoftwareRule
                ? 'Select one or more operations to apply to matching tenant software records.'
                : isApplicationRule
                  ? 'Select one or more operations to apply to matching cloud applications.'
                  : 'Select one or more operations to apply to matching devices.'}
            </p>
          </CardHeader>
          <CardContent className="space-y-4">
            {supportsOwnerOnlyRule ? (
              <OperationEditor
                type="AssignOwnerTeam"
                label="Assign Owner Team"
                description={isApplicationRule
                  ? 'Set the owning team on matching cloud application records.'
                  : 'Set the owning team on matching tenant software inventory records.'}
                options={teams.map((t) => ({ value: t.id, label: t.name }))}
                paramKey="teamId"
                operations={operations}
                onChange={setOperations}
              />
            ) : (
              <>
                <OperationEditor
                  type="AssignOwnerTeam"
                  label="Assign Owner Team"
                  description="Set the owning team on matching devices."
                  options={teams.map((t) => ({ value: t.id, label: t.name }))}
                  paramKey="teamId"
                  operations={operations}
                  onChange={setOperations}
                />
                <OperationEditor
                  type="AssignSecurityProfile"
                  label="Assign Security Profile"
                  description="Set the security profile on matching devices for environmental CVSS scoring."
                  options={securityProfiles.map((p) => ({
                    value: p.id,
                    label: p.name,
                  }))}
                  paramKey="securityProfileId"
                  operations={operations}
                  onChange={setOperations}
                />
                <OperationEditor
                  type="AssignTeam"
                  label="Assign Team"
                  description="Set the fallback assignment group for task routing on matching devices."
                  options={teams.map((t) => ({ value: t.id, label: t.name }))}
                  paramKey="teamId"
                  operations={operations}
                  onChange={setOperations}
                />
                <OperationEditor
                  type="SetCriticality"
                  label="Set Criticality"
                  description="Set the canonical device criticality used by risk scoring and executive reporting."
                  options={criticalityOptions}
                  paramKey="criticality"
                  operations={operations}
                  onChange={setOperations}
                />
                <OperationEditor
                  type="AssignBusinessLabel"
                  label="Assign Business Label"
                  description="Apply a tenant business label to matching devices so dashboards and summaries use recognizable business context."
                  options={businessLabels.filter((label) => label.isActive).map((label) => ({
                    value: label.id,
                    label: label.name,
                  }))}
                  paramKey="businessLabelId"
                  operations={operations}
                  onChange={setOperations}
                />
                <OperationEditor
                  type="AssignScanProfile"
                  label="Assign Scan Profile"
                  description="Assign an authenticated scan profile to matching devices for on-prem host scanning."
                  options={scanProfiles.map((p) => ({
                    value: p.id,
                    label: p.name,
                  }))}
                  paramKey="scanProfileId"
                  operations={operations}
                  onChange={setOperations}
                />
              </>
            )}
          </CardContent>
        </Card>
      )}

      {step === 3 && (
        <Card className="rounded-2xl border-border/70">
          <CardHeader>
            <CardTitle>Summary</CardTitle>
            <p className="text-sm text-muted-foreground">
              Review your rule before saving.
            </p>
          </CardHeader>
          <CardContent className="space-y-4">
            <InsetPanel className="space-y-2 px-4 py-3">
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                Asset type
              </p>
              <p className="text-sm font-medium">{assetType}</p>
            </InsetPanel>

            <InsetPanel className="space-y-2 px-4 py-3">
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                Name
              </p>
              <p className="text-sm font-medium">{name}</p>
              {description && (
                <p className="text-sm text-muted-foreground">{description}</p>
              )}
            </InsetPanel>

            <InsetPanel className="space-y-2 px-4 py-3">
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                Filter
              </p>
              <FilterSummary group={filter} />
            </InsetPanel>

            <InsetPanel className="space-y-2 px-4 py-3">
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                Operations
              </p>
              {operations.length > 0 ? (
                <>
                  <div className="space-y-1">
                    {operations.map((op, i) => (
                      <div key={i} className="flex items-center gap-2 text-sm">
                        <Badge variant="outline" className="text-[10px]">
                          {op.type === "AssignSecurityProfile"
                            ? "Security Profile"
                            : op.type === "AssignOwnerTeam"
                              ? "Owner Team"
                            : op.type === "AssignTeam"
                              ? "Team"
                              : op.type === "AssignBusinessLabel"
                                ? "Business Label"
                                : op.type === "AssignScanProfile"
                                  ? "Scan Profile"
                                  : "Criticality"}
                        </Badge>
                        <span>
                          {describeOperationTarget(op, securityProfiles, businessLabels, teams, scanProfiles)}
                        </span>
                      </div>
                    ))}
                  </div>
                  <p className="text-xs text-muted-foreground">
                    {buildPreviewHeadline(assetType, preview?.count ?? 0, operations, securityProfiles, businessLabels, teams, scanProfiles)}
                  </p>
                </>
              ) : (
                <p className="text-sm text-muted-foreground">
                  {isSoftwareRule
                    ? 'No operations selected.'
                    : isApplicationRule
                    ? 'No operations selected.'
                    : 'No operations selected.'}
                </p>
              )}
            </InsetPanel>

            {saveMutation.isError && (
              <p className="text-sm text-destructive">
                Failed to save rule. Please try again.
              </p>
            )}
          </CardContent>
        </Card>
      )}

      {/* Navigation */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={step === 0}
            onClick={() => setStep((s) => s - 1)}
          >
            <ArrowLeft className="size-3.5" />
            Back
          </Button>
          <Button
            type="button"
            variant="ghost"
            className="text-muted-foreground"
            onClick={() => {
              if (onCancel) {
                onCancel()
                return
              }

              void router.navigate({
                to: "/admin/device-rules",
                search: { page: 1, pageSize: 25 },
              })
            }}
          >
            Cancel
          </Button>
        </div>
        {step < 3 ? (
          <Button
            type="button"
            disabled={!canContinue()}
            onClick={() => setStep((s) => s + 1)}
          >
            Continue
            <ArrowRight className="size-3.5" />
          </Button>
        ) : (
          <Button
            type="button"
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending}
          >
            {saveMutation.isPending && (
              <Loader2 className="size-3.5 animate-spin" />
            )}
            {mode === "create" ? "Create rule" : "Save changes"}
          </Button>
        )}
      </div>
    </section>
  );
}

function buildPreviewHeadline(
  assetType: AssetRuleAssetType,
  count: number,
  operations: DeviceRuleOperation[],
  securityProfiles: SecurityProfile[],
  businessLabels: BusinessLabel[],
  teams: TeamItem[],
  scanProfiles: ScanProfile[],
) {
  const assetLabel = describePreviewAsset(assetType)

  if (operations.length === 0) {
    return `${count} ${assetLabel}${count !== 1 ? 's' : ''} match`
  }

  const operationDescriptions = buildOperationImpactLines(operations, securityProfiles, businessLabels, teams, scanProfiles)
  if (operationDescriptions.length === 1) {
    return `This rule will ${operationDescriptions[0]} for ${count} ${assetLabel}${count !== 1 ? 's' : ''}.`
  }

  return `This rule will affect ${count} ${assetLabel}${count !== 1 ? 's' : ''} with ${operationDescriptions.length} operations.`
}

function describePreviewAsset(assetType: AssetRuleAssetType) {
  switch (assetType) {
    case 'Software':
      return 'software asset'
    case 'Application':
      return 'cloud application'
    default:
      return 'device'
  }
}

function buildOperationImpactLines(
  operations: DeviceRuleOperation[],
  securityProfiles: SecurityProfile[],
  businessLabels: BusinessLabel[],
  teams: TeamItem[],
  scanProfiles: ScanProfile[],
) {
  return operations.map((operation) => {
    switch (operation.type) {
      case 'AssignSecurityProfile':
        return `set security profile to ${describeOperationTarget(operation, securityProfiles, businessLabels, teams, scanProfiles)}`
      case 'AssignOwnerTeam':
        return `assign owner team ${describeOperationTarget(operation, securityProfiles, businessLabels, teams, scanProfiles)}`
      case 'AssignTeam':
        return `assign fallback team ${describeOperationTarget(operation, securityProfiles, businessLabels, teams, scanProfiles)}`
      case 'AssignBusinessLabel':
        return `apply business label ${describeOperationTarget(operation, securityProfiles, businessLabels, teams, scanProfiles)}`
      case 'AssignScanProfile':
        return `assign scan profile ${describeOperationTarget(operation, securityProfiles, businessLabels, teams, scanProfiles)}`
      case 'SetCriticality':
        return `set criticality to ${describeOperationTarget(operation, securityProfiles, businessLabels, teams, scanProfiles)}`
      default:
        return `apply ${operation.type}`
    }
  })
}

function describeOperationTarget(
  operation: DeviceRuleOperation,
  securityProfiles: SecurityProfile[],
  businessLabels: BusinessLabel[],
  teams: TeamItem[],
  scanProfiles: ScanProfile[],
) {
  if (operation.type === 'AssignSecurityProfile') {
    return securityProfiles.find((profile) => profile.id === operation.parameters.securityProfileId)?.name
      ?? operation.parameters.securityProfileId
  }

  if (operation.type === 'AssignTeam') {
    return teams.find((team) => team.id === operation.parameters.teamId)?.name
      ?? operation.parameters.teamId
  }

  if (operation.type === 'AssignOwnerTeam') {
    return teams.find((team) => team.id === operation.parameters.teamId)?.name
      ?? operation.parameters.teamId
  }

  if (operation.type === 'AssignBusinessLabel') {
    return businessLabels.find((label) => label.id === operation.parameters.businessLabelId)?.name
      ?? operation.parameters.businessLabelId
  }

  if (operation.type === 'SetCriticality') {
    return operation.parameters.criticality
  }

  if (operation.type === 'AssignScanProfile') {
    return scanProfiles.find((p) => p.id === operation.parameters.scanProfileId)?.name
      ?? operation.parameters.scanProfileId
  }

  return operation.type
}

function OperationEditor({
  type,
  label,
  description,
  options,
  paramKey,
  operations,
  onChange,
}: {
  type: string
  label: string
  description: string
  options: { value: string; label: string }[]
  paramKey: string
  operations: DeviceRuleOperation[]
  onChange: (ops: DeviceRuleOperation[]) => void
}) {
  const existing = operations.find((op) => op.type === type)
  const isActive = !!existing
  const selectedValue = existing?.parameters[paramKey] ?? ''
  const selectedOption = options.find((opt) => opt.value === selectedValue)

  const toggle = () => {
    if (isActive) {
      onChange(operations.filter((op) => op.type !== type))
    } else {
      onChange([...operations, { type, parameters: { [paramKey]: options[0]?.value ?? '' } }])
    }
  }

  const updateValue = (value: string) => {
    onChange(
      operations.map((op) =>
        op.type === type ? { ...op, parameters: { [paramKey]: value } } : op,
      ),
    )
  }

  return (
    <InsetPanel className={`space-y-3 px-4 py-3 ${isActive ? 'border-primary/30' : ''}`}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-medium">{label}</p>
          <p className="text-xs text-muted-foreground">{description}</p>
        </div>
        <Button
          type="button"
          variant={isActive ? 'default' : 'outline'}
          size="sm"
          className="h-7 text-xs"
          onClick={toggle}
        >
          {isActive ? 'Active' : 'Add'}
        </Button>
      </div>
      {isActive && (
        <Select value={selectedValue} onValueChange={(v) => v && updateValue(v)}>
          <SelectTrigger className="h-9 rounded-lg text-sm">
            <SelectValue placeholder={`Select ${label.toLowerCase()}...`}>
              {selectedOption?.label}
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            {options.map((opt) => (
              <SelectItem key={opt.value} value={opt.value}>
                {opt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </InsetPanel>
  )
}

function FilterSummary({ group }: { group: FilterGroup }) {
  if (group.conditions.length === 0) {
    return <p className="text-sm text-muted-foreground">No conditions defined</p>
  }

  return (
    <div className="space-y-1 text-sm">
      {group.conditions.map((child, i) => (
        <div key={i} className="flex items-start gap-1">
          {i > 0 && (
            <span className="shrink-0 font-mono text-xs font-bold text-muted-foreground">
              {group.operator}
            </span>
          )}
          {child.type === 'group' ? (
            <div className="rounded border border-border/50 px-2 py-1">
              <FilterSummary group={child} />
            </div>
          ) : (
            <span>
              {child.field}{' '}
              <span className="text-muted-foreground">{child.operator.toLowerCase()}</span>{' '}
              <span className="font-medium">&ldquo;{child.value}&rdquo;</span>
            </span>
          )}
        </div>
      ))}
    </div>
  )
}
