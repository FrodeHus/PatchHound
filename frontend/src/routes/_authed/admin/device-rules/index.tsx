import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createFileRoute, Link, useRouter } from "@tanstack/react-router";
import { toast } from "sonner";
import type { ColumnDef } from "@tanstack/react-table";
import { Play, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import {
  deleteDeviceRule,
  fetchDeviceRules,
  runDeviceRules,
  updateDeviceRule,
} from "@/api/device-rules.functions";
import type { DeviceRule } from "@/api/device-rules.schemas";
import { Button } from "@/components/ui/button";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { SortableColumnHeader } from "@/components/ui/sortable-column-header";
import { DataTable } from "@/components/ui/data-table";
import {
  DataTableWorkbench,
  DataTableEmptyState,
} from "@/components/ui/data-table-workbench";
import { PaginationControls } from "@/components/ui/pagination-controls";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { baseListSearchSchema } from "@/routes/-list-search";

export const Route = createFileRoute("/_authed/admin/device-rules/")({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) =>
    fetchDeviceRules({ data: { page: deps.page, pageSize: deps.pageSize } }),
  component: DeviceRulesPage,
});

const columns: ColumnDef<DeviceRule>[] = [
  {
    accessorKey: "priority",
    header: ({ column }) => <SortableColumnHeader column={column} title="#" />,
    size: 50,
    cell: ({ row }) => (
      <span className="font-mono text-xs text-muted-foreground">
        {row.original.priority}
      </span>
    ),
  },
  {
    accessorKey: "name",
    header: ({ column }) => <SortableColumnHeader column={column} title="Name" />,
    cell: ({ row }) => (
      <Link
        to="/admin/device-rules/$id"
        params={{ id: row.original.id }}
        className="font-medium text-primary hover:underline"
      >
        {row.original.name}
      </Link>
    ),
  },
  {
    accessorKey: "description",
    header: ({ column }) => <SortableColumnHeader column={column} title="Description" />,
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.description ?? "-"}
      </span>
    ),
  },
  {
    accessorKey: "enabled",
    header: ({ column }) => <SortableColumnHeader column={column} title="Status" />,
    size: 90,
    cell: ({ row }) => (
      <Badge variant={row.original.enabled ? "default" : "secondary"}>
        {row.original.enabled ? "Enabled" : "Disabled"}
      </Badge>
    ),
  },
  {
    accessorKey: "lastMatchCount",
    header: ({ column }) => <SortableColumnHeader column={column} title="Last Match" />,
    size: 100,
    cell: ({ row }) => (
      <span className="text-sm">
        {row.original.lastMatchCount !== null
          ? `${row.original.lastMatchCount} devices`
          : "-"}
      </span>
    ),
  },
  {
    accessorKey: "lastExecutedAt",
    header: ({ column }) => <SortableColumnHeader column={column} title="Last Run" />,
    size: 150,
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.lastExecutedAt
          ? new Date(row.original.lastExecutedAt).toLocaleDateString()
          : "Never"}
      </span>
    ),
  },
];

function DeviceRulesPage() {
  const loaderData = Route.useLoaderData();
  const search = Route.useSearch();
  const navigate = Route.useNavigate();
  const router = useRouter();
  const queryClient = useQueryClient();
  const [deleteTarget, setDeleteTarget] = useState<DeviceRule | null>(null);

  const query = useQuery({
    queryKey: ["device-rules", search.page, search.pageSize],
    queryFn: () =>
      fetchDeviceRules({
        data: { page: search.page, pageSize: search.pageSize },
      }),
    initialData: loaderData,
  });

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => deleteDeviceRule({ data: { id } }),
    onSuccess: async () => {
      setDeleteTarget(null);
      toast.success("Rule deleted");
      await router.invalidate();
    },
    onError: () => {
      toast.error("Failed to delete rule");
    },
  });

  const runMutation = useMutation({
    mutationFn: async () => runDeviceRules(),
    onSuccess: async () => {
      toast.success("Rules evaluation started");
      await router.invalidate();
    },
    onError: () => {
      toast.error("Failed to start evaluation");
    },
  });

  const toggleMutation = useMutation({
    mutationFn: async (rule: DeviceRule) =>
      updateDeviceRule({
        data: {
          id: rule.id,
          name: rule.name,
          description: rule.description ?? undefined,
          enabled: !rule.enabled,
          filterDefinition: rule.filterDefinition,
          operations: rule.operations,
        },
      }),
    onMutate: (rule) => {
      const queryKey = ["device-rules", search.page, search.pageSize];
      queryClient.setQueryData(
        queryKey,
        (old: typeof query.data | undefined) => {
          if (!old) return old;
          return {
            ...old,
            items: old.items.map((item) =>
              item.id === rule.id ? { ...item, enabled: !item.enabled } : item,
            ),
          };
        },
      );
    },
    onSuccess: async (_data, rule) => {
      toast.success(rule.enabled ? "Rule disabled" : "Rule enabled");
      await router.invalidate();
    },
    onError: () => {
      toast.error("Failed to update rule");
    },
  });

  const actionsColumn: ColumnDef<DeviceRule> = {
    id: "actions",
    enableSorting: false,
    size: 120,
    cell: ({ row }) => (
      <div className="flex items-center gap-1">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-7 text-xs"
          onClick={() => toggleMutation.mutate(row.original)}
        >
          {row.original.enabled ? "Disable" : "Enable"}
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
  };

  return (
    <section className="space-y-5">
      <DataTableWorkbench
        title="Device Rules"
        description="Rules run in priority order after each ingestion. First match wins per device."
        totalCount={query.data.totalCount}
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
          <Link to="/admin/device-rules/new">
            <Button type="button" size="sm">
              <Plus className="size-3.5" />
              Create rule
            </Button>
          </Link>
        </div>
        <DataTable
          columns={[...columns, actionsColumn]}
          data={query.data.items}
          emptyState={
            <DataTableEmptyState
              title="No device rules yet"
              description="Create your first rule to automatically classify and tag devices after ingestion."
            />
          }
        />
        <PaginationControls
          page={query.data.page}
          pageSize={query.data.pageSize}
          totalCount={query.data.totalCount}
          totalPages={query.data.totalPages}
          onPageChange={(page) =>
            void navigate({ search: (prev) => ({ ...prev, page }) })
          }
          onPageSizeChange={(pageSize) =>
            void navigate({
              search: (prev) => ({ ...prev, pageSize, page: 1 }),
            })
          }
        />
      </DataTableWorkbench>

      <Dialog
        open={deleteTarget !== null}
        onOpenChange={() => setDeleteTarget(null)}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete rule</DialogTitle>
            <DialogDescription>
              Are you sure you want to delete &ldquo;{deleteTarget?.name}
              &rdquo;? This action cannot be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setDeleteTarget(null)}
            >
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={() =>
                deleteTarget && deleteMutation.mutate(deleteTarget.id)
              }
              disabled={deleteMutation.isPending}
            >
              Delete
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
}
