import { useEffect, useMemo, useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { Asset } from '@/api/assets.schemas'
import {
  DataTableActiveFilters,
  DataTableEmptyState,
  DataTableField,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from "@/components/ui/data-table-workbench";
import { Input } from '@/components/ui/input'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { DataTable } from '@/components/ui/data-table'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { SearchIcon } from 'lucide-react'
import { WorkbenchFilterDrawer, WorkbenchFilterSection } from '@/components/ui/workbench-filter-drawer'

type AssetManagementTableProps = {
  assets: Asset[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  isUpdating: boolean
  selectedAssetId: string | null
  searchValue: string
  assetTypeFilter: string
  criticalityFilter: string
  ownerTypeFilter: string
  deviceGroupFilter: string
  unassignedOnly: boolean
  onSearchChange: (search: string) => void
  onAssetTypeFilterChange: (assetType: string) => void
  onCriticalityFilterChange: (criticality: string) => void
  onOwnerTypeFilterChange: (ownerType: string) => void
  onDeviceGroupFilterChange: (deviceGroup: string) => void
  onUnassignedOnlyChange: (value: boolean) => void
  onApplyStructuredFilters: (filters: {
    assetType: string
    criticality: string
    ownerType: string
    deviceGroup: string
    unassignedOnly: boolean
  }) => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onClearFilters: () => void
  onSelectAsset: (assetId: string) => void
  onAssignOwner: (assetId: string, ownerType: 'User' | 'Team', ownerId: string) => void
  onSetCriticality: (assetId: string, criticality: string) => void
}

const criticalityOptions = ['Low', 'Medium', 'High', 'Critical']
const assetTypeOptions = ['All', 'Device', 'Software', 'CloudResource']
const ownershipFilterOptions = [
  { label: 'Any ownership', value: '' },
  { label: 'Assigned user', value: 'User' },
  { label: 'Assignment group', value: 'Team' },
]

export function AssetManagementTable({
  assets,
  totalCount,
  page,
  pageSize,
  totalPages,
  isUpdating,
  searchValue,
  assetTypeFilter,
  criticalityFilter,
  ownerTypeFilter,
  deviceGroupFilter,
  unassignedOnly,
  onSearchChange,
  onAssetTypeFilterChange,
  onCriticalityFilterChange,
  onOwnerTypeFilterChange,
  onDeviceGroupFilterChange,
  onUnassignedOnlyChange,
  onApplyStructuredFilters,
  onPageChange,
  onPageSizeChange,
  onClearFilters,
  onSelectAsset,
  onAssignOwner,
  onSetCriticality,
}: AssetManagementTableProps) {
  const [ownerType, setOwnerType] = useState<"User" | "Team">("User");
  const [ownerId, setOwnerId] = useState("");
  const [searchInput, setSearchInput] = useState(searchValue);
  const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false)
  const [draftFilters, setDraftFilters] = useState({
    assetType: assetTypeFilter,
    criticality: criticalityFilter,
    ownerType: ownerTypeFilter,
    deviceGroup: deviceGroupFilter,
    unassignedOnly,
  })

  useEffect(() => {
    setSearchInput(searchValue);
  }, [searchValue]);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      if (searchInput !== searchValue) {
        onSearchChange(searchInput);
      }
    }, 350);

    return () => {
      window.clearTimeout(timeoutId);
    };
  }, [onSearchChange, searchInput, searchValue]);

  useEffect(() => {
    if (!isFilterDrawerOpen) {
      setDraftFilters({
        assetType: assetTypeFilter,
        criticality: criticalityFilter,
        ownerType: ownerTypeFilter,
        deviceGroup: deviceGroupFilter,
        unassignedOnly,
      })
    }
  }, [assetTypeFilter, criticalityFilter, deviceGroupFilter, isFilterDrawerOpen, ownerTypeFilter, unassignedOnly])

  const activeFilters = useMemo(
    () =>
      [
        searchValue
          ? {
              key: "search",
              label: `Search: ${searchValue}`,
              onClear: () => {
                onSearchChange("");
              },
            }
          : null,
        assetTypeFilter
          ? {
              key: "type",
              label: `Type: ${assetTypeFilter}`,
              onClear: () => {
                onAssetTypeFilterChange("");
              },
            }
          : null,
        criticalityFilter
          ? {
              key: "criticality",
              label: `Criticality: ${criticalityFilter}`,
              onClear: () => {
                onCriticalityFilterChange("");
              },
            }
          : null,
        ownerTypeFilter
          ? {
              key: "ownerType",
              label:
                ownerTypeFilter === "Team"
                  ? "Owned by assignment group"
                  : "Owned by user",
              onClear: () => {
                onOwnerTypeFilterChange("");
              },
            }
          : null,
        deviceGroupFilter
          ? {
              key: "deviceGroup",
              label: `Device Group: ${deviceGroupFilter}`,
              onClear: () => {
                onDeviceGroupFilterChange("");
              },
            }
          : null,
        unassignedOnly
          ? {
              key: "unassigned",
              label: "Unassigned only",
              onClear: () => {
                onUnassignedOnlyChange(false);
              },
            }
          : null,
      ].filter((value): value is NonNullable<typeof value> => value !== null),
    [
      assetTypeFilter,
      criticalityFilter,
      deviceGroupFilter,
      onAssetTypeFilterChange,
      onCriticalityFilterChange,
      onDeviceGroupFilterChange,
      onOwnerTypeFilterChange,
      onSearchChange,
      onUnassignedOnlyChange,
      ownerTypeFilter,
      searchValue,
      unassignedOnly,
    ],
  );

  const activeStructuredFilterCount = useMemo(
    () =>
      [
        assetTypeFilter,
        criticalityFilter,
        ownerTypeFilter,
        deviceGroupFilter,
        unassignedOnly ? 'unassigned' : '',
      ].filter(Boolean).length,
    [assetTypeFilter, criticalityFilter, deviceGroupFilter, ownerTypeFilter, unassignedOnly],
  )

  const columns = useMemo<ColumnDef<Asset>[]>(
    () => [
      {
        accessorKey: "name",
        header: "Asset",
        cell: ({ row }) => (
          <div className="space-y-1">
            <button
              type="button"
              className="text-left font-medium tracking-tight underline decoration-border/70 underline-offset-4 transition hover:decoration-foreground"
              onClick={() => {
                onSelectAsset(row.original.id);
              }}
            >
              {row.original.name}
            </button>
            <p className="font-mono text-[11px] text-muted-foreground">
              {row.original.externalId}
            </p>
          </div>
        ),
      },
      {
        accessorKey: "assetType",
        header: "Type",
        cell: ({ row }) => (
          <Badge
            variant="outline"
            className="rounded-full border-border/70 bg-background/70"
          >
            {row.original.assetType}
          </Badge>
        ),
      },
      {
        accessorKey: "deviceGroupName",
        header: "Device Group",
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.assetType === "Device"
              ? row.original.deviceGroupName ?? "Unknown"
              : "Not applicable"}
          </span>
        ),
      },
      {
        accessorKey: "securityProfileName",
        header: "Security profile",
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.securityProfileName ?? "No profile"}
          </span>
        ),
      },
      {
        id: "ownership",
        header: "Ownership",
        cell: ({ row }) => renderOwnership(row.original),
      },
      {
        accessorKey: "recurringVulnerabilityCount",
        header: "Recurring",
        cell: ({ row }) =>
          row.original.recurringVulnerabilityCount > 0 ? (
            <Badge className="rounded-full border border-amber-300/70 bg-amber-50 text-amber-900 hover:bg-amber-50">
              {row.original.recurringVulnerabilityCount} recurring
            </Badge>
          ) : (
            <span className="text-sm text-muted-foreground">None</span>
          ),
      },
      {
        accessorKey: "criticality",
        header: "Criticality",
        cell: ({ row }) => (
          <Select
            value={row.original.criticality}
            onValueChange={(value) => {
              onSetCriticality(
                row.original.id,
                value ?? row.original.criticality,
              );
            }}
          >
            <SelectTrigger
              className="h-8 min-w-[126px] rounded-xl border-border/70 bg-background/80 px-3"
              onClick={(event) => {
                event.stopPropagation();
              }}
              disabled={isUpdating}
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
              {criticalityOptions.map((value) => (
                <SelectItem key={value} value={value}>
                  {value}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ),
      },
      {
        accessorKey: "vulnerabilityCount",
        header: "Vulnerabilities",
        cell: ({ row }) => (
          <span className="text-sm font-medium">
            {row.original.vulnerabilityCount}
          </span>
        ),
      },
      {
        id: "actions",
        header: () => <div className="text-right">Actions</div>,
        cell: ({ row }) => (
          <div className="text-right">
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isUpdating || ownerId.trim().length === 0}
              onClick={(event) => {
                event.stopPropagation();
                onAssignOwner(row.original.id, ownerType, ownerId);
              }}
            >
              Assign owner
            </Button>
          </div>
        ),
      },
    ],
    [
      isUpdating,
      onAssignOwner,
      onSelectAsset,
      onSetCriticality,
      ownerId,
      ownerType,
    ],
  );

  return (
    <DataTableWorkbench
      title="Assets"
      description="Scan device and software inventory, narrow the working set, and open the inspector from the asset name."
      totalCount={totalCount}
    >
      <DataTableToolbar>
        <DataTableToolbarRow className="items-end gap-4">
          <DataTableField
            label="Search"
            hint="Matches displayed asset name, DNS name, and external ID."
            className="flex-1"
          >
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={searchInput}
                onChange={(event) => {
                  setSearchInput(event.target.value);
                }}
                placeholder="Search assets"
                className="h-10 rounded-xl border-border/70 bg-background/80 pl-10"
              />
            </div>
          </DataTableField>
          <Button
            type="button"
            variant="outline"
            className="h-10 rounded-xl border-border/70 bg-background/80 px-4"
            onClick={() => {
              setDraftFilters({
                assetType: assetTypeFilter,
                criticality: criticalityFilter,
                ownerType: ownerTypeFilter,
                deviceGroup: deviceGroupFilter,
                unassignedOnly,
              })
              setIsFilterDrawerOpen(true)
            }}
          >
            {activeStructuredFilterCount > 0 ? `Filters (${activeStructuredFilterCount})` : 'Filters...'}
          </Button>
        </DataTableToolbarRow>

        <DataTableToolbarRow className="gap-4">
          <DataTableActiveFilters
            filters={activeFilters}
            onClearAll={onClearFilters}
            className="flex-1"
          />

          <div className="flex flex-col gap-2 rounded-[20px] border border-border/70 bg-background/55 px-4 py-3 lg:min-w-[340px]">
            <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted-foreground">
              Quick owner assignment
            </p>
            <div className="flex flex-col gap-2 sm:flex-row">
              <Select
                value={ownerType}
                onValueChange={(value) => {
                  setOwnerType(value === "Team" ? "Team" : "User");
                }}
              >
                <SelectTrigger className="h-9 min-w-[150px] rounded-xl border-border/70 bg-background/80 px-3">
                  <SelectValue placeholder="Owner type" />
                </SelectTrigger>
                <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                  <SelectItem value="User">User</SelectItem>
                  <SelectItem value="Team">Assignment group</SelectItem>
                </SelectContent>
              </Select>
              <Input
                value={ownerId}
                onChange={(event) => {
                  setOwnerId(event.target.value);
                }}
                placeholder="Owner or assignment group ID"
                className="h-9 rounded-xl border-border/70 bg-background/80"
              />
            </div>
          </div>
        </DataTableToolbarRow>
      </DataTableToolbar>

      <WorkbenchFilterDrawer
        open={isFilterDrawerOpen}
        onOpenChange={setIsFilterDrawerOpen}
        title="Asset Filters"
        description="Apply inventory, ownership, and risk filters without crowding the workbench."
        activeCount={activeStructuredFilterCount}
        onResetDraft={() => {
          setDraftFilters({
            assetType: '',
            criticality: '',
            ownerType: '',
            deviceGroup: '',
            unassignedOnly: false,
          })
        }}
        onApply={() => {
          onApplyStructuredFilters(draftFilters)
          setIsFilterDrawerOpen(false)
        }}
      >
        <WorkbenchFilterSection
          title="Inventory"
          description="Narrow the device and software estate to the inventory slice you want to inspect."
        >
          <DataTableField label="Type">
            <Select
              value={draftFilters.assetType || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  assetType: nextValue === "all" ? "" : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any asset type" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                {assetTypeOptions.map((option) => (
                  <SelectItem key={option} value={option === "All" ? "all" : option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>

          <DataTableField
            label="Device Group"
            hint="Matches Defender device group name or immutable group ID."
          >
            <Input
              value={draftFilters.deviceGroup}
              onChange={(event) => {
                setDraftFilters((current) => ({
                  ...current,
                  deviceGroup: event.target.value,
                }))
              }}
              placeholder="Filter device group"
              className="h-10 rounded-xl border-border/70 bg-background/80"
            />
          </DataTableField>
        </WorkbenchFilterSection>

        <WorkbenchFilterSection
          title="Ownership"
          description="Focus on assets by ownership model or isolate items that still need routing."
        >
          <DataTableField label="Owner Type">
            <Select
              value={draftFilters.ownerType || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  ownerType: nextValue === "all" ? "" : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any ownership" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                {ownershipFilterOptions.map((option) => (
                  <SelectItem key={option.label} value={option.value || "all"}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>

          <label className="flex items-center gap-3 rounded-xl border border-border/70 bg-background/60 px-3 py-3 text-sm">
            <input
              type="checkbox"
              checked={draftFilters.unassignedOnly}
              onChange={(event) => {
                setDraftFilters((current) => ({
                  ...current,
                  unassignedOnly: event.target.checked,
                }))
              }}
              className="size-4 rounded border-border/70"
            />
            <span>Show unassigned assets only</span>
          </label>
        </WorkbenchFilterSection>

        <WorkbenchFilterSection
          title="Risk"
          description="Reduce the list to the criticality band you want to work on."
        >
          <DataTableField label="Criticality">
            <Select
              value={draftFilters.criticality || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  criticality: nextValue === "all" ? "" : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any criticality" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any criticality</SelectItem>
                {criticalityOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>
        </WorkbenchFilterSection>
      </WorkbenchFilterDrawer>

      {assets.length === 0 ? (
        <DataTableEmptyState
          title="No assets match the current view"
          description="Try clearing one or more filters, or broaden the search to bring more assets into the working set."
        />
      ) : (
        <div className="overflow-hidden rounded-[24px] border border-border/70 bg-background/30">
          <DataTable
            columns={columns}
            data={assets}
            getRowId={(row) => row.id}
            className="min-w-[1080px]"
          />
        </div>
      )}

      <PaginationControls
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        totalPages={totalPages}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
      />
    </DataTableWorkbench>
  );
}

function renderOwnership(asset: Asset) {
  if (asset.ownerTeamId) {
    return (
      <div className="space-y-1">
        <p className="text-sm font-medium">Assignment group</p>
        <p className="text-xs text-muted-foreground">Routed through group ownership</p>
      </div>
    )
  }

  if (asset.ownerUserId) {
    return (
      <div className="space-y-1">
        <p className="text-sm font-medium">Direct user</p>
        <p className="text-xs text-muted-foreground">Assigned to an individual owner</p>
      </div>
    )
  }

  return (
    <div className="space-y-1">
      <p className="text-sm font-medium">Unassigned</p>
      <p className="text-xs text-muted-foreground">No owner or assignment group yet</p>
    </div>
  )
}
