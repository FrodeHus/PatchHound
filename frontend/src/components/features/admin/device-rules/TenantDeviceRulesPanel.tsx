import { useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import type { ColumnDef } from '@tanstack/react-table'
import { Loader2, Play, Plus, SquarePen, Trash2 } from 'lucide-react'
import {
  deleteDeviceRule,
  fetchDeviceRule,
  fetchDeviceRules,
  runDeviceRules,
  updateDeviceRule,
} from '@/api/device-rules.functions'
import type { DeviceRule } from '@/api/device-rules.schemas'
import { fetchScanProfiles } from '@/api/authenticated-scans.functions'
import { fetchBusinessLabels } from '@/api/business-labels.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { fetchTeams } from '@/api/teams.functions'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import {
  DataTableEmptyState,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { DeviceRuleWizard } from './DeviceRuleWizard'

type TenantDeviceRulesPanelProps = {
  tenantId: string
  tenantName: string
  mode?: string
  ruleId?: string
  onSearchChange?: (patch: { mode?: 'edit' | 'new'; ruleId?: string }) => void
}

const columns: ColumnDef<DeviceRule>[] = [
  {
    accessorKey: 'priority',
    header: ({ column }) => <SortableColumnHeader column={column} title="#" />,
    size: 50,
    cell: ({ row }) => (
      <span className="font-mono text-xs text-muted-foreground">
        {row.original.priority}
      </span>
    ),
  },
  {
    accessorKey: 'name',
    header: ({ column }) => <SortableColumnHeader column={column} title="Name" />,
    cell: ({ row }) => <span className="font-medium">{row.original.name}</span>,
  },
  {
    accessorKey: 'description',
    header: ({ column }) => <SortableColumnHeader column={column} title="Description" />,
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.description ?? '-'}
      </span>
    ),
  },
  {
    accessorKey: 'enabled',
    header: ({ column }) => <SortableColumnHeader column={column} title="Status" />,
    size: 90,
    cell: ({ row }) => (
      <Badge variant={row.original.enabled ? 'default' : 'secondary'}>
        {row.original.enabled ? 'Enabled' : 'Disabled'}
      </Badge>
    ),
  },
  {
    accessorKey: 'lastMatchCount',
    header: ({ column }) => <SortableColumnHeader column={column} title="Last Match" />,
    size: 100,
    cell: ({ row }) => (
      <span className="text-sm">
        {row.original.lastMatchCount !== null
          ? `${row.original.lastMatchCount} devices`
          : '-'}
      </span>
    ),
  },
  {
    accessorKey: 'lastExecutedAt',
    header: ({ column }) => <SortableColumnHeader column={column} title="Last Run" />,
    size: 150,
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.lastExecutedAt
          ? new Date(row.original.lastExecutedAt).toLocaleDateString()
          : 'Never'}
      </span>
    ),
  },
]

export function TenantDeviceRulesPanel({
  tenantId,
  tenantName,
  mode,
  ruleId,
  onSearchChange,
}: TenantDeviceRulesPanelProps) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [deleteTarget, setDeleteTarget] = useState<DeviceRule | null>(null)
  const wizardMode = mode === 'new' || mode === 'edit' ? mode : null

  const rulesQuery = useQuery({
    queryKey: ['tenant-device-rules', tenantId, page, pageSize],
    queryFn: () => fetchDeviceRules({ data: { tenantId, page, pageSize } }),
  })

  const dependenciesEnabled = wizardMode !== null

  const securityProfilesQuery = useQuery({
    queryKey: ['tenant-device-rule-security-profiles', tenantId],
    queryFn: () => fetchSecurityProfiles({ data: { tenantId, pageSize: 100 } }),
    enabled: dependenciesEnabled,
  })

  const businessLabelsQuery = useQuery({
    queryKey: ['tenant-device-rule-business-labels', tenantId],
    queryFn: () => fetchBusinessLabels({ data: { tenantId } }),
    enabled: dependenciesEnabled,
  })

  const teamsQuery = useQuery({
    queryKey: ['tenant-device-rule-teams', tenantId],
    queryFn: () => fetchTeams({ data: { tenantId, pageSize: 100 } }),
    enabled: dependenciesEnabled,
  })

  const scanProfilesQuery = useQuery({
    queryKey: ['tenant-device-rule-scan-profiles', tenantId],
    queryFn: () => fetchScanProfiles({ data: { tenantId, pageSize: 100 } }),
    enabled: dependenciesEnabled,
  })

  const ruleQuery = useQuery({
    queryKey: ['tenant-device-rule', tenantId, ruleId],
    queryFn: () => fetchDeviceRule({ data: { id: ruleId!, tenantId } }),
    enabled: wizardMode === 'edit' && Boolean(ruleId),
  })
  const editRule = wizardMode === 'edit' ? ruleQuery.data : undefined

  const refreshRules = async () => {
    await rulesQuery.refetch()
    if (wizardMode === 'edit' && ruleId) {
      await ruleQuery.refetch()
    }
  }

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => deleteDeviceRule({ data: { id, tenantId } }),
    onSuccess: async () => {
      setDeleteTarget(null)
      toast.success('Rule deleted')
      await refreshRules()
      onSearchChange?.({ mode: undefined, ruleId: undefined })
    },
    onError: () => {
      toast.error('Failed to delete rule')
    },
  })

  const runMutation = useMutation({
    mutationFn: async () => runDeviceRules({ data: { tenantId } }),
    onSuccess: async () => {
      toast.success('Rules evaluation started')
      await refreshRules()
    },
    onError: () => {
      toast.error('Failed to start evaluation')
    },
  })

  const toggleMutation = useMutation({
    mutationFn: async (rule: DeviceRule) =>
      updateDeviceRule({
        data: {
          tenantId,
          id: rule.id,
          name: rule.name,
          description: rule.description ?? undefined,
          enabled: !rule.enabled,
          filterDefinition: rule.filterDefinition,
          operations: rule.operations,
        },
      }),
    onSuccess: async (_data, rule) => {
      toast.success(rule.enabled ? 'Rule disabled' : 'Rule enabled')
      await refreshRules()
    },
    onError: () => {
      toast.error('Failed to update rule')
    },
  })

  const isWizardLoading = wizardMode === 'edit'
    ? ruleQuery.isLoading
      || securityProfilesQuery.isLoading
      || businessLabelsQuery.isLoading
      || teamsQuery.isLoading
      || scanProfilesQuery.isLoading
    : wizardMode === 'new'
      ? securityProfilesQuery.isLoading
        || businessLabelsQuery.isLoading
        || teamsQuery.isLoading
        || scanProfilesQuery.isLoading
      : false

  const isWizardError = wizardMode === 'edit'
    ? ruleQuery.isError
      || securityProfilesQuery.isError
      || businessLabelsQuery.isError
      || teamsQuery.isError
      || scanProfilesQuery.isError
    : wizardMode === 'new'
      ? securityProfilesQuery.isError
        || businessLabelsQuery.isError
        || teamsQuery.isError
        || scanProfilesQuery.isError
      : false

  const actionsColumn: ColumnDef<DeviceRule> = useMemo(() => ({
    id: 'actions',
    enableSorting: false,
    size: 180,
    cell: ({ row }) => (
      <div className="flex items-center gap-1">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-7 text-xs"
          onClick={() => onSearchChange?.({ mode: 'edit', ruleId: row.original.id })}
        >
          <SquarePen className="size-3.5" />
          Edit
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-7 text-xs"
          onClick={() => toggleMutation.mutate(row.original)}
        >
          {row.original.enabled ? 'Disable' : 'Enable'}
        </Button>
        <Tooltip>
          <TooltipTrigger
            render={
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-7 text-destructive"
                onClick={() => setDeleteTarget(row.original)}
              />
            }
          >
            <Trash2 className="size-3.5" />
          </TooltipTrigger>
          <TooltipContent>Delete</TooltipContent>
        </Tooltip>
      </div>
    ),
  }), [onSearchChange, toggleMutation])

  if (wizardMode) {
    if (isWizardLoading) {
      return (
        <section className="flex min-h-64 items-center justify-center rounded-2xl border border-border/70 bg-card/85">
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" />
            Loading device rule configuration...
          </div>
        </section>
      )
    }

    if (
      isWizardError
      || !securityProfilesQuery.data
      || !businessLabelsQuery.data
      || !teamsQuery.data
      || !scanProfilesQuery.data
      || (wizardMode === 'edit' && !ruleQuery.data)
    ) {
      return (
        <section className="rounded-2xl border border-border/70 bg-card/85 p-6">
          <p className="text-sm text-destructive">
            Failed to load the device rule editor for {tenantName}.
          </p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="mt-4"
            onClick={() => onSearchChange?.({ mode: undefined, ruleId: undefined })}
          >
            Back to rules
          </Button>
        </section>
      )
    }

    return (
      <section className="space-y-5">
        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Device Rules</p>
          <h2 className="text-2xl font-semibold tracking-[-0.04em]">
            {wizardMode === 'new'
              ? `Create Rule for ${tenantName}`
              : `Edit: ${editRule?.name ?? 'Rule'}`}
          </h2>
        </div>
        <DeviceRuleWizard
          mode={wizardMode === 'new' ? 'create' : 'edit'}
          tenantId={tenantId}
          initialData={editRule}
          securityProfiles={securityProfilesQuery.data.items}
          businessLabels={businessLabelsQuery.data}
          teams={teamsQuery.data.items}
          scanProfiles={scanProfilesQuery.data.items}
          onCancel={() => onSearchChange?.({ mode: undefined, ruleId: undefined })}
          onSaved={async () => {
            await refreshRules()
            onSearchChange?.({ mode: undefined, ruleId: undefined })
          }}
        />
      </section>
    )
  }

  return (
    <section className="space-y-5">
      <DataTableWorkbench
        title="Device Rules"
        description={`Rules for ${tenantName} run in priority order after each ingestion. First match wins per device.`}
        totalCount={rulesQuery.data?.totalCount}
      >
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => runMutation.mutate()}
            disabled={runMutation.isPending}
          >
            <Play className="size-3.5" />
            Run now
          </Button>
          <Button
            type="button"
            size="sm"
            onClick={() => onSearchChange?.({ mode: 'new', ruleId: undefined })}
          >
            <Plus className="size-3.5" />
            Create rule
          </Button>
        </div>

        {rulesQuery.isLoading ? (
          <div className="flex min-h-40 items-center justify-center text-sm text-muted-foreground">
            <Loader2 className="mr-2 size-4 animate-spin" />
            Loading device rules...
          </div>
        ) : rulesQuery.isError || !rulesQuery.data ? (
          <p className="text-sm text-destructive">
            Failed to load device rules for {tenantName}.
          </p>
        ) : (
          <>
            <DataTable
              columns={[...columns, actionsColumn]}
              data={rulesQuery.data.items}
              emptyState={
                <DataTableEmptyState
                  title="No device rules yet"
                  description="Create your first rule to automatically classify and tag devices after ingestion."
                />
              }
            />
            <PaginationControls
              page={rulesQuery.data.page}
              pageSize={rulesQuery.data.pageSize}
              totalCount={rulesQuery.data.totalCount}
              totalPages={Math.max(1, Math.ceil(rulesQuery.data.totalCount / rulesQuery.data.pageSize))}
              onPageChange={setPage}
              onPageSizeChange={(value) => {
                setPage(1)
                setPageSize(value)
              }}
            />
          </>
        )}
      </DataTableWorkbench>

      <Dialog open={deleteTarget !== null} onOpenChange={(open) => !open && setDeleteTarget(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete device rule</DialogTitle>
            <DialogDescription>
              {deleteTarget
                ? `Delete "${deleteTarget.name}" for ${tenantName}? This will re-evaluate the remaining rules for the tenant.`
                : 'Delete this device rule?'}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setDeleteTarget(null)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
              disabled={deleteMutation.isPending}
            >
              Delete
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}
