import { useEffect, useMemo, useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { Device } from '@/api/devices.schemas'
import type { BusinessLabel } from '@/api/business-labels.schemas'
import {
  DataTableActiveFilters,
  DataTableEmptyState,
  DataTableField,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from "@/components/ui/data-table-workbench";
import { Input } from '@/components/ui/input'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
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
import { ExternalLinkIcon, SearchIcon } from "lucide-react";
import { WorkbenchFilterDrawer, WorkbenchFilterSection } from '@/components/ui/workbench-filter-drawer'
import { toneBadge } from '@/lib/tone-classes'

// Phase 1 canonical cleanup (Task 15): device-native management table.
// The legacy AssetType filter/column was removed alongside the /assets
// surface — /devices is device-only and the Software/CloudResource
// filters will return in later phases on their own surfaces.

type DeviceManagementTableProps = {
  devices: Device[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  isUpdating: boolean;
  selectedDeviceId: string | null;
  title?: string;
  description?: string;
  searchPlaceholder?: string;
  searchValue: string;
  criticalityFilter: string;
  businessLabelIdFilter: string;
  availableBusinessLabels: BusinessLabel[];
  ownerTypeFilter: string;
  deviceGroupFilter: string;
  healthStatusFilter: string;
  onboardingStatusFilter: string;
  riskScoreFilter: string;
  exposureLevelFilter: string;
  tagFilter: string;
  unassignedOnly: boolean;
  onSearchChange: (search: string) => void;
  onCriticalityFilterChange: (criticality: string) => void;
  onBusinessLabelFilterChange: (businessLabelId: string) => void;
  onOwnerTypeFilterChange: (ownerType: string) => void;
  onDeviceGroupFilterChange: (deviceGroup: string) => void;
  onHealthStatusFilterChange: (healthStatus: string) => void;
  onOnboardingStatusFilterChange: (onboardingStatus: string) => void;
  onRiskScoreFilterChange: (riskScore: string) => void;
  onExposureLevelFilterChange: (exposureLevel: string) => void;
  onTagFilterChange: (tag: string) => void;
  onUnassignedOnlyChange: (value: boolean) => void;
  onApplyStructuredFilters: (filters: {
    criticality: string;
    businessLabelId: string;
    ownerType: string;
    deviceGroup: string;
    healthStatus: string;
    onboardingStatus: string;
    riskScore: string;
    exposureLevel: string;
    tag: string;
    unassignedOnly: boolean;
  }) => void;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
  onClearFilters: () => void;
  onSelectDevice: (deviceId: string) => void;
  onAssignOwner: (
    deviceId: string,
    ownerType: "User" | "Team",
    ownerId: string,
  ) => void;
  onSetCriticality: (deviceId: string, criticality: string) => void;
};

const criticalityOptions = ['Low', 'Medium', 'High', 'Critical']
const healthStatusOptions = ['Active', 'Inactive', 'ImpairedCommunication', 'NoSensorData', 'NoSensorDataImpairedCommunication']
const onboardingStatusOptions = ['Onboarded', 'CanBeOnboarded', 'Unsupported', 'InsufficientInfo']
const riskScoreOptions = ['None', 'Low', 'Medium', 'High']
const exposureLevelOptions = ['None', 'Low', 'Medium', 'High']
const ownershipFilterOptions = [
  { label: 'Any ownership', value: '' },
  { label: 'Assigned user', value: 'User' },
  { label: 'Assignment group', value: 'Team' },
]

function getCurrentDraftFilters({
  criticalityFilter,
  businessLabelIdFilter,
  ownerTypeFilter,
  deviceGroupFilter,
  healthStatusFilter,
  onboardingStatusFilter,
  riskScoreFilter,
  exposureLevelFilter,
  tagFilter,
  unassignedOnly,
}: {
  criticalityFilter: string
  businessLabelIdFilter: string
  ownerTypeFilter: string
  deviceGroupFilter: string
  healthStatusFilter: string
  onboardingStatusFilter: string
  riskScoreFilter: string
  exposureLevelFilter: string
  tagFilter: string
  unassignedOnly: boolean
}) {
  return {
    criticality: criticalityFilter,
    businessLabelId: businessLabelIdFilter,
    ownerType: ownerTypeFilter,
    deviceGroup: deviceGroupFilter,
    healthStatus: healthStatusFilter,
    onboardingStatus: onboardingStatusFilter,
    riskScore: riskScoreFilter,
    exposureLevel: exposureLevelFilter,
    tag: tagFilter,
    unassignedOnly,
  }
}

export function DeviceManagementTable({
  devices,
  totalCount,
  page,
  pageSize,
  totalPages,
  isUpdating,
  searchValue,
  title = "Devices",
  description = "Scan the device fleet, narrow the working set, and open the inspector from the device name.",
  searchPlaceholder = "Search devices",
  criticalityFilter,
  businessLabelIdFilter,
  availableBusinessLabels,
  ownerTypeFilter,
  deviceGroupFilter,
  healthStatusFilter,
  onboardingStatusFilter,
  riskScoreFilter,
  exposureLevelFilter,
  tagFilter,
  unassignedOnly,
  onSearchChange,
  onCriticalityFilterChange,
  onBusinessLabelFilterChange,
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
  onSelectDevice,
  onAssignOwner,
  onSetCriticality,
}: DeviceManagementTableProps) {
  const [ownerType, _setOwnerType] = useState<"User" | "Team">("User");
  const [ownerId, _setOwnerId] = useState("");
  const [searchInputState, setSearchInputState] = useState({
    source: searchValue,
    value: searchValue,
  });
  const searchInput =
    searchInputState.source === searchValue ? searchInputState.value : searchValue;
  const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false);
  const [draftFilters, setDraftFilters] = useState(() => getCurrentDraftFilters({
    criticalityFilter,
    businessLabelIdFilter,
    ownerTypeFilter,
    deviceGroupFilter,
    healthStatusFilter,
    onboardingStatusFilter,
    riskScoreFilter,
    exposureLevelFilter,
    tagFilter,
    unassignedOnly,
  }));

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

  const currentDraftFilters = getCurrentDraftFilters({
    criticalityFilter,
    businessLabelIdFilter,
    ownerTypeFilter,
    deviceGroupFilter,
    healthStatusFilter,
    onboardingStatusFilter,
    riskScoreFilter,
    exposureLevelFilter,
    tagFilter,
    unassignedOnly,
  });

  const selectedBusinessLabelName = useMemo(
    () =>
      availableBusinessLabels.find((label) => label.id === businessLabelIdFilter)?.name
      ?? businessLabelIdFilter,
    [availableBusinessLabels, businessLabelIdFilter],
  )

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
        criticalityFilter
          ? {
              key: "criticality",
              label: `Criticality: ${criticalityFilter}`,
              onClear: () => {
                onCriticalityFilterChange("");
              },
            }
          : null,
        businessLabelIdFilter
          ? {
              key: "businessLabel",
              label: `Business label: ${selectedBusinessLabelName}`,
              onClear: () => {
                onBusinessLabelFilterChange("");
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
      businessLabelIdFilter,
      criticalityFilter,
      deviceGroupFilter,
      exposureLevelFilter,
      healthStatusFilter,
      onBusinessLabelFilterChange,
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
      selectedBusinessLabelName,
      tagFilter,
      unassignedOnly,
    ],
  );

  const activeStructuredFilterCount = useMemo(
    () =>
      [
        criticalityFilter,
        businessLabelIdFilter,
        ownerTypeFilter,
        deviceGroupFilter,
        healthStatusFilter,
        onboardingStatusFilter,
        riskScoreFilter,
        exposureLevelFilter,
        tagFilter,
        unassignedOnly ? "unassigned" : "",
      ].filter(Boolean).length,
    [
      businessLabelIdFilter,
      criticalityFilter,
      deviceGroupFilter,
      exposureLevelFilter,
      healthStatusFilter,
      onboardingStatusFilter,
      ownerTypeFilter,
      riskScoreFilter,
      tagFilter,
      unassignedOnly,
    ],
  );

  const columns = useMemo<ColumnDef<Device>[]>(() => {
    const columns: ColumnDef<Device>[] = [
      {
        accessorKey: "name",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Device" />
        ),
        cell: ({ row }) => (
          <div className="space-y-1">
            <div className="flex items-center gap-1.5">
              <button
                type="button"
                className="text-left font-medium tracking-tight underline decoration-border/70 underline-offset-4 transition hover:decoration-foreground"
                onClick={() => {
                  onSelectDevice(row.original.id);
                }}
              >
                {row.original.name}
              </button>
              <Link
                to="/devices/$id"
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
            {row.original.businessLabels.length > 0 ? (
              <div className="flex flex-wrap gap-1">
                {row.original.businessLabels.slice(0, 2).map((label) => (
                  <Badge
                    key={label.id}
                    variant="outline"
                    className="rounded-full border-border/70 bg-background/70 text-[10px]"
                  >
                    {label.name}
                  </Badge>
                ))}
                {row.original.businessLabels.length > 2 ? (
                  <Badge
                    variant="outline"
                    className="rounded-full border-border/70 bg-background/70 text-[10px]"
                  >
                    +{row.original.businessLabels.length - 2}
                  </Badge>
                ) : null}
              </div>
            ) : null}
          </div>
        ),
      },
      {
        accessorKey: "groupName",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Device Group" />
        ),
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.groupName ?? "Unknown"}
          </span>
        ),
      },
      {
        accessorKey: "currentRiskScore",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Current risk" />
        ),
        cell: ({ row }) => {
          const score = row.original.currentRiskScore;
          if (score == null)
            return <span className="text-muted-foreground">—</span>;
          const tone =
            score >= 900
              ? "danger"
              : score >= 750
                ? "warning"
                : score >= 500
                  ? "info"
                  : "success";
          return (
            <span className={`font-medium tabular-nums ${toneBadge(tone)}`}>
              {score.toFixed(0)}
            </span>
          );
        },
      },
      {
        accessorKey: "healthStatus",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Health" />
        ),
        cell: ({ row }) => {
          const value = row.original.healthStatus;
          if (!value) return <span className="text-muted-foreground">—</span>;
          return (
            <Badge
              variant="outline"
              className="rounded-full border-border/70 bg-background/70"
            >
              {value}
            </Badge>
          );
        },
      },
      {
        accessorKey: "onboardingStatus",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Onboarding" />
        ),
        cell: ({ row }) => {
          const value = row.original.onboardingStatus;
          if (!value) return <span className="text-muted-foreground">—</span>;
          return (
            <Badge
              variant="outline"
              className="rounded-full border-border/70 bg-background/70"
            >
              {value}
            </Badge>
          );
        },
      },
      {
        accessorKey: "riskScore",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Risk" />
        ),
        cell: ({ row }) => {
          const value = row.original.riskScore;
          if (!value) return <span className="text-muted-foreground">—</span>;
          return (
            <Badge
              variant="outline"
              className="rounded-full border-border/70 bg-background/70"
            >
              {value}
            </Badge>
          );
        },
      },
      {
        accessorKey: "exposureLevel",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Exposure" />
        ),
        cell: ({ row }) => {
          const value = row.original.exposureLevel;
          if (!value) return <span className="text-muted-foreground">—</span>;
          return (
            <Badge
              variant="outline"
              className="rounded-full border-border/70 bg-background/70"
            >
              {value}
            </Badge>
          );
        },
      },
      {
        accessorKey: "tags",
        header: "Tags",
        enableSorting: false,
        cell: ({ row }) => {
          const tags = row.original.tags;
          if (!tags?.length)
            return <span className="text-muted-foreground">—</span>;
          return (
            <div className="flex flex-wrap gap-1">
              {tags.map((tag) => (
                <Badge key={tag} variant="secondary" className="text-xs">
                  {tag}
                </Badge>
              ))}
            </div>
          );
        },
      },
      {
        accessorKey: "securityProfileName",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Security profile" />
        ),
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.securityProfileName ?? "No profile"}
          </span>
        ),
      },
      {
        id: "ownership",
        header: "Ownership",
        enableSorting: false,
        cell: ({ row }) => renderOwnership(row.original),
      },
      {
        accessorKey: "recurringVulnerabilityCount",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Recurring" />
        ),
        cell: ({ row }) =>
          row.original.recurringVulnerabilityCount > 0 ? (
            <Badge
              className={`rounded-full border hover:bg-transparent ${toneBadge("warning")}`}
            >
              {row.original.recurringVulnerabilityCount} recurring
            </Badge>
          ) : (
            <span className="text-sm text-muted-foreground">None</span>
          ),
      },
      {
        accessorKey: "criticality",
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Criticality" />
        ),
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
        header: ({ column }) => (
          <SortableColumnHeader column={column} title="Vulnerabilities" />
        ),
        cell: ({ row }) => (
          <span className="text-sm font-medium">
            {row.original.vulnerabilityCount}
          </span>
        ),
      },
      {
        id: "actions",
        header: () => <div className="text-right">Actions</div>,
        enableSorting: false,
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
    ];

    return columns;
  }, [
    isUpdating,
    onAssignOwner,
    onSelectDevice,
    onSetCriticality,
    ownerId,
    ownerType,
  ]);

  return (
    <DataTableWorkbench
      title={title}
      description={description}
      totalCount={totalCount}
    >
      <DataTableToolbar>
        <DataTableToolbarRow className="items-end gap-4">
          <DataTableField
            label="Search"
            hint="Matches displayed device name, DNS name, and external ID."
            className="flex-1"
          >
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={searchInput}
                onChange={(event) => {
                  setSearchInputState({
                    source: searchValue,
                    value: event.target.value,
                  });
                }}
                placeholder={searchPlaceholder}
                className="h-10 rounded-xl border-border/70 bg-background/80 pl-10"
              />
            </div>
          </DataTableField>
          <Button
            type="button"
            variant="outline"
            className="h-10 rounded-xl border-border/70 bg-background/80 px-4"
            onClick={() => {
              setDraftFilters(currentDraftFilters);
              setIsFilterDrawerOpen(true);
            }}
          >
            {activeStructuredFilterCount > 0
              ? `Filters (${activeStructuredFilterCount})`
              : "Filters..."}
          </Button>
        </DataTableToolbarRow>

        <DataTableToolbarRow className="gap-4">
          <DataTableActiveFilters
            filters={activeFilters}
            onClearAll={onClearFilters}
            className="flex-1"
          />
        </DataTableToolbarRow>
      </DataTableToolbar>

      <WorkbenchFilterDrawer
        open={isFilterDrawerOpen}
        onOpenChange={setIsFilterDrawerOpen}
        title="Device Filters"
        description="Apply inventory, ownership, and risk filters without crowding the workbench."
        activeCount={activeStructuredFilterCount}
        onResetDraft={() => {
          setDraftFilters({
            criticality: "",
            businessLabelId: "",
            ownerType: "",
            deviceGroup: "",
            healthStatus: "",
            onboardingStatus: "",
            riskScore: "",
            exposureLevel: "",
            tag: "",
            unassignedOnly: false,
          });
        }}
        onApply={() => {
          onApplyStructuredFilters(draftFilters);
          setIsFilterDrawerOpen(false);
        }}
      >
        <WorkbenchFilterSection
          title="Inventory"
          description="Narrow the device estate to the inventory slice you want to inspect."
        >
          <DataTableField
            label="Business Label"
            hint="Filter by an exact tenant business label."
          >
            <Select
              value={draftFilters.businessLabelId || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  businessLabelId: nextValue === "all" ? "" : nextValue,
                }));
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any business label" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any business label</SelectItem>
                {availableBusinessLabels
                  .filter((label) => label.isActive)
                  .map((label) => (
                    <SelectItem key={label.id} value={label.id}>
                      {label.name}
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
                }));
              }}
              placeholder="Filter device group"
              className="h-10 rounded-xl border-border/70 bg-background/80"
            />
          </DataTableField>
        </WorkbenchFilterSection>

        <WorkbenchFilterSection
          title="Ownership"
          description="Focus on devices by ownership model or isolate items that still need routing."
        >
          <DataTableField label="Owner Type">
            <Select
              value={draftFilters.ownerType || "all"}
              onValueChange={(value) => {
                const nextValue = value ?? "all";
                setDraftFilters((current) => ({
                  ...current,
                  ownerType: nextValue === "all" ? "" : nextValue,
                }));
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

          <label className="flex items-center gap-3 rounded-xl border border-border/70 bg-background/50 px-3 py-3 text-sm">
            <input
              type="checkbox"
              checked={draftFilters.unassignedOnly}
              onChange={(event) => {
                setDraftFilters((current) => ({
                  ...current,
                  unassignedOnly: event.target.checked,
                }));
              }}
              className="size-4 rounded border-border/70"
            />
            <span>Show unassigned devices only</span>
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
                }));
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
                }));
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
                }));
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
                }));
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
                }));
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
            hint="Filter devices by a specific Defender machine tag."
          >
            <Input
              value={draftFilters.tag}
              onChange={(event) => {
                setDraftFilters((current) => ({
                  ...current,
                  tag: event.target.value,
                }));
              }}
              placeholder="Filter by tag"
              className="h-10 rounded-xl border-border/70 bg-background/80"
            />
          </DataTableField>
        </WorkbenchFilterSection>
      </WorkbenchFilterDrawer>

      {devices.length === 0 ? (
        <DataTableEmptyState
          title="No devices match the current view"
          description="Try clearing one or more filters, or broaden the search to bring more devices into the working set."
        />
      ) : (
        <div className="overflow-hidden rounded-2xl border border-border/70 bg-background/30">
          <DataTable
            columns={columns}
            data={devices}
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

function renderOwnership(device: Device) {
  if (device.ownerTeamId) {
    return (
      <div className="space-y-1">
        <p className="text-sm font-medium">Assignment group</p>
        <p className="text-xs text-muted-foreground">Routed through group ownership</p>
      </div>
    )
  }

  if (device.ownerUserId) {
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
