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
import { Link } from '@tanstack/react-router'
import { ExternalLinkIcon, SearchIcon } from 'lucide-react'
import { WorkbenchFilterDrawer, WorkbenchFilterSection } from '@/components/ui/workbench-filter-drawer'
import { toneBadge } from '@/lib/tone-classes'

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
  healthStatusFilter: string
  onboardingStatusFilter: string
  riskScoreFilter: string
  exposureLevelFilter: string
  tagFilter: string
  unassignedOnly: boolean
  onSearchChange: (search: string) => void
  onAssetTypeFilterChange: (assetType: string) => void
  onCriticalityFilterChange: (criticality: string) => void
  onOwnerTypeFilterChange: (ownerType: string) => void
  onDeviceGroupFilterChange: (deviceGroup: string) => void
  onHealthStatusFilterChange: (healthStatus: string) => void
  onOnboardingStatusFilterChange: (onboardingStatus: string) => void
  onRiskScoreFilterChange: (riskScore: string) => void
  onExposureLevelFilterChange: (exposureLevel: string) => void
  onTagFilterChange: (tag: string) => void
  onUnassignedOnlyChange: (value: boolean) => void
  onApplyStructuredFilters: (filters: {
    assetType: string
    criticality: string
    ownerType: string
    deviceGroup: string
    healthStatus: string
    onboardingStatus: string
    riskScore: string
    exposureLevel: string
    tag: string
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
const healthStatusOptions = ['Active', 'Inactive', 'ImpairedCommunication', 'NoSensorData', 'NoSensorDataImpairedCommunication']
const onboardingStatusOptions = ['Onboarded', 'CanBeOnboarded', 'Unsupported', 'InsufficientInfo']
const riskScoreOptions = ['None', 'Low', 'Medium', 'High']
const exposureLevelOptions = ['None', 'Low', 'Medium', 'High']
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
  healthStatusFilter,
  onboardingStatusFilter,
  riskScoreFilter,
  exposureLevelFilter,
  tagFilter,
  unassignedOnly,
  onSearchChange,
  onAssetTypeFilterChange,
  onCriticalityFilterChange,
  onOwnerTypeFilterChange,
  onDeviceGroupFilterChange,
  onHealthStatusFilterChange,
  onOnboardingStatusFilterChange,
  onRiskScoreFilterChange,
  onExposureLevelFilterChange,
  onTagFilterChange,
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
    healthStatus: healthStatusFilter,
    onboardingStatus: onboardingStatusFilter,
    riskScore: riskScoreFilter,
    exposureLevel: exposureLevelFilter,
    tag: tagFilter,
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
        healthStatus: healthStatusFilter,
        onboardingStatus: onboardingStatusFilter,
        riskScore: riskScoreFilter,
        exposureLevel: exposureLevelFilter,
        tag: tagFilter,
        unassignedOnly,
      })
    }
  }, [assetTypeFilter, criticalityFilter, deviceGroupFilter, exposureLevelFilter, healthStatusFilter, isFilterDrawerOpen, onboardingStatusFilter, ownerTypeFilter, riskScoreFilter, tagFilter, unassignedOnly])

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
        healthStatusFilter
          ? {
              key: "healthStatus",
              label: `Health: ${healthStatusFilter}`,
              onClear: () => {
                onHealthStatusFilterChange("");
              },
            }
          : null,
        onboardingStatusFilter
          ? {
              key: "onboardingStatus",
              label: `Onboarding: ${onboardingStatusFilter}`,
              onClear: () => {
                onOnboardingStatusFilterChange("");
              },
            }
          : null,
        riskScoreFilter
          ? {
              key: "riskScore",
              label: `Risk: ${riskScoreFilter}`,
              onClear: () => {
                onRiskScoreFilterChange("");
              },
            }
          : null,
        exposureLevelFilter
          ? {
              key: "exposureLevel",
              label: `Exposure: ${exposureLevelFilter}`,
              onClear: () => {
                onExposureLevelFilterChange("");
              },
            }
          : null,
        tagFilter
          ? {
              key: "tag",
              label: `Tag: ${tagFilter}`,
              onClear: () => {
                onTagFilterChange("");
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
      exposureLevelFilter,
      healthStatusFilter,
      onAssetTypeFilterChange,
      onCriticalityFilterChange,
      onDeviceGroupFilterChange,
      onExposureLevelFilterChange,
      onHealthStatusFilterChange,
      onOnboardingStatusFilterChange,
      onOwnerTypeFilterChange,
      onRiskScoreFilterChange,
      onSearchChange,
      onTagFilterChange,
      onUnassignedOnlyChange,
      onboardingStatusFilter,
      ownerTypeFilter,
      riskScoreFilter,
      searchValue,
      tagFilter,
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
        healthStatusFilter,
        onboardingStatusFilter,
        riskScoreFilter,
        exposureLevelFilter,
        tagFilter,
        unassignedOnly ? 'unassigned' : '',
      ].filter(Boolean).length,
    [assetTypeFilter, criticalityFilter, deviceGroupFilter, exposureLevelFilter, healthStatusFilter, onboardingStatusFilter, ownerTypeFilter, riskScoreFilter, tagFilter, unassignedOnly],
  )

  const columns = useMemo<ColumnDef<Asset>[]>(
    () => [
      {
        accessorKey: "name",
        header: "Asset",
        cell: ({ row }) => (
          <div className="space-y-1">
            <div className="flex items-center gap-1.5">
              <button
                type="button"
                className="text-left font-medium tracking-tight underline decoration-border/70 underline-offset-4 transition hover:decoration-foreground"
                onClick={() => {
                  onSelectAsset(row.original.id);
                }}
              >
                {row.original.name}
              </button>
              <Link
                to="/assets/$id"
                params={{ id: row.original.id }}
                className="shrink-0 text-muted-foreground transition hover:text-foreground"
                title="Open full detail view"
              >
                <ExternalLinkIcon className="size-3.5" />
              </Link>
            </div>
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
        accessorKey: "healthStatus",
        header: "Health",
        cell: ({ row }) => {
          const value = row.original.healthStatus
          if (!value) return <span className="text-muted-foreground">—</span>
          return <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">{value}</Badge>
        },
      },
      {
        accessorKey: "onboardingStatus",
        header: "Onboarding",
        cell: ({ row }) => {
          const value = row.original.onboardingStatus
          if (!value) return <span className="text-muted-foreground">—</span>
          return <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">{value}</Badge>
        },
      },
      {
        accessorKey: "riskScore",
        header: "Risk",
        cell: ({ row }) => {
          const value = row.original.riskScore
          if (!value) return <span className="text-muted-foreground">—</span>
          return <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">{value}</Badge>
        },
      },
      {
        accessorKey: "exposureLevel",
        header: "Exposure",
        cell: ({ row }) => {
          const value = row.original.exposureLevel
          if (!value) return <span className="text-muted-foreground">—</span>
          return <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">{value}</Badge>
        },
      },
      {
        accessorKey: "tags",
        header: "Tags",
        cell: ({ row }) => {
          const tags = row.original.tags
          if (!tags?.length) return <span className="text-muted-foreground">—</span>
          return (
            <div className="flex flex-wrap gap-1">
              {tags.map((tag) => (
                <Badge key={tag} variant="secondary" className="text-xs">{tag}</Badge>
              ))}
            </div>
          )
        },
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
            <Badge className={`rounded-full border hover:bg-transparent ${toneBadge('warning')}`}>
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
                healthStatus: healthStatusFilter,
                onboardingStatus: onboardingStatusFilter,
                riskScore: riskScoreFilter,
                exposureLevel: exposureLevelFilter,
                tag: tagFilter,
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
            healthStatus: '',
            onboardingStatus: '',
            riskScore: '',
            exposureLevel: '',
            tag: '',
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

        <WorkbenchFilterSection
          title="Device Risk"
          description="Filter by Defender health status, risk score, exposure level, and device tags."
        >
          <DataTableField label="Health Status">
            <Select
              value={draftFilters.healthStatus || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  healthStatus: nextValue === "all" ? "" : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any health status" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any health status</SelectItem>
                {healthStatusOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>

          <DataTableField label="Onboarding Status">
            <Select
              value={draftFilters.onboardingStatus || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  onboardingStatus: nextValue === "all" ? "" : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any onboarding status" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any onboarding status</SelectItem>
                {onboardingStatusOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>

          <DataTableField label="Risk Score">
            <Select
              value={draftFilters.riskScore || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  riskScore: nextValue === "all" ? "" : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any risk score" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any risk score</SelectItem>
                {riskScoreOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>

          <DataTableField label="Exposure Level">
            <Select
              value={draftFilters.exposureLevel || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  exposureLevel: nextValue === "all" ? "" : nextValue,
                }))
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any exposure level" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any exposure level</SelectItem>
                {exposureLevelOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>

          <DataTableField
            label="Tag"
            hint="Filter assets by a specific Defender machine tag."
          >
            <Input
              value={draftFilters.tag}
              onChange={(event) => {
                setDraftFilters((current) => ({
                  ...current,
                  tag: event.target.value,
                }))
              }}
              placeholder="Filter by tag"
              className="h-10 rounded-xl border-border/70 bg-background/80"
            />
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
