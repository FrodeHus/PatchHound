import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { toast } from 'sonner'
import { PenSquare, Plus, Tags, Trash2 } from 'lucide-react'
import {
  createBusinessLabel,
  deleteBusinessLabel,
  fetchBusinessLabels,
  updateBusinessLabel,
} from '@/api/business-labels.functions'
import type { BusinessLabel, BusinessLabelWeightCategory, SaveBusinessLabel } from '@/api/business-labels.schemas'
import { WEIGHT_CATEGORY_CONFIG, businessLabelWeightCategorySchema } from '@/api/business-labels.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'

export const Route = createFileRoute('/_authed/admin/business-labels')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (
      !activeRoles.includes('GlobalAdmin') &&
      !activeRoles.includes('SecurityManager') &&
      !activeRoles.includes('CustomerAdmin')
    ) {
      throw new Error('Unauthorized')
    }
  },
  loader: () => fetchBusinessLabels({ data: {} }),
  component: BusinessLabelsPage,
})

type LabelDraft = SaveBusinessLabel & { id?: string }

const emptyDraft = (): LabelDraft => ({
  name: '',
  description: '',
  color: '#2563eb',
  isActive: true,
  weightCategory: 'Normal',
})

function BusinessLabelsPage() {
  const initialLabels = Route.useLoaderData()
  const queryClient = useQueryClient()
  const [editorOpen, setEditorOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<BusinessLabel | null>(null)
  const [draft, setDraft] = useState<LabelDraft>(emptyDraft)

  const labelsQuery = useQuery({
    queryKey: ['business-labels'],
    queryFn: () => fetchBusinessLabels({ data: {} }),
    initialData: initialLabels,
    staleTime: 30_000,
  })

  const saveMutation = useMutation({
    mutationFn: async (value: LabelDraft) => {
      if (value.id) {
        await updateBusinessLabel({
          data: {
            id: value.id,
            name: value.name,
            description: value.description,
            color: value.color,
            isActive: value.isActive,
          },
        })
        return
      }
      await createBusinessLabel({ data: value })
    },
    onSuccess: async () => {
      toast.success(draft.id ? 'Business label updated' : 'Business label created')
      setEditorOpen(false)
      setDraft(emptyDraft())
      await queryClient.invalidateQueries({ queryKey: ['business-labels'] })
    },
    onError: () => {
      toast.error('Failed to save business label')
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      await deleteBusinessLabel({ data: { id } })
    },
    onSuccess: async () => {
      toast.success('Business label deleted')
      setDeleteTarget(null)
      await queryClient.invalidateQueries({ queryKey: ['business-labels'] })
    },
    onError: () => {
      toast.error('Failed to delete business label')
      setDeleteTarget(null)
    },
  })

  const labels = useMemo<BusinessLabel[]>(
    () => labelsQuery.data ?? [],
    [labelsQuery.data],
  );
  const sortedLabels = useMemo(
    () => [...labels].sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.name.localeCompare(b.name)),
    [labels],
  )

  return (
    <>
      <section className="space-y-5">
        <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="space-y-2">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Tenant operations
              </p>
              <h1 className="text-3xl font-semibold tracking-[-0.04em]">
                Business Labels
              </h1>
              <p className="max-w-3xl text-sm text-muted-foreground">
                Create recognizable business context for assets so remediation,
                dashboards, and AI summaries can describe impact in terms people
                understand.
              </p>
            </div>
            <Button
              type="button"
              className="rounded-full"
              onClick={() => {
                setDraft(emptyDraft());
                setEditorOpen(true);
              }}
            >
              <Plus className="mr-2 size-4" />
              New label
            </Button>
          </div>
        </div>

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(20rem,0.6fr)]">
          <section className="space-y-4">
            {sortedLabels.length === 0 ? (
              <Card className="rounded-3xl border-border/70">
                <CardContent className="flex min-h-56 flex-col items-center justify-center gap-3 text-center">
                  <span className="flex size-14 items-center justify-center rounded-full border border-border/70 bg-background text-primary">
                    <Tags className="size-6" />
                  </span>
                  <div className="space-y-1">
                    <p className="text-lg font-semibold">
                      No business labels yet
                    </p>
                    <p className="max-w-md text-sm text-muted-foreground">
                      Start with a few recognizable labels like Production,
                      Finance, Executive, or Customer-facing.
                    </p>
                  </div>
                </CardContent>
              </Card>
            ) : (
              sortedLabels.map((label) => (
                <Card key={label.id} className="rounded-3xl border-border/70">
                  <CardHeader className="space-y-4">
                    <div className="flex items-start justify-between gap-4">
                      <div className="space-y-3">
                        <div className="flex flex-wrap items-center gap-2">
                          <BusinessLabelChip
                            name={label.name}
                            color={label.color}
                            weightCategory={label.weightCategory}
                          />
                          <Badge
                            variant="outline"
                            className="rounded-full border-border/70 bg-background/50"
                          >
                            {label.isActive ? "Active" : "Inactive"}
                          </Badge>
                        </div>
                        <div>
                          <CardTitle>{label.name}</CardTitle>
                          <CardDescription className="mt-2">
                            {label.description?.trim() ||
                              "No description provided yet."}
                          </CardDescription>
                          <p className="mt-1 text-xs text-muted-foreground">
                            {WEIGHT_CATEGORY_CONFIG[label.weightCategory].description}
                          </p>
                        </div>
                      </div>
                      <div className="flex gap-2">
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="rounded-full"
                          onClick={() => {
                            setDraft({
                              id: label.id,
                              name: label.name,
                              description: label.description,
                              color: label.color,
                              isActive: label.isActive,
                              weightCategory: label.weightCategory,
                            });
                            setEditorOpen(true);
                          }}
                        >
                          <PenSquare className="mr-2 size-4" />
                          Edit
                        </Button>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="rounded-full text-destructive hover:text-destructive"
                          onClick={() => setDeleteTarget(label)}
                        >
                          <Trash2 className="mr-2 size-4" />
                          Delete
                        </Button>
                      </div>
                    </div>
                  </CardHeader>
                </Card>
              ))
            )}
          </section>

          <Card className="rounded-3xl border-border/70">
            <CardHeader>
              <CardTitle>Recommended label set</CardTitle>
              <CardDescription>
                Keep labels recognizable and stable. Use them as business
                context, not as a substitute for criticality or tenant access.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3 text-sm text-muted-foreground">
              <p>
                Good starting labels: Production, Finance, HR, Executive, Tier
                0, Customer-facing, Shared Services.
              </p>
              <p>
                Use a small set first so reporting stays understandable and
                assets do not accumulate overlapping labels.
              </p>
              <p>
                Inactive labels stay visible on already-labeled assets but
                cannot be newly assigned.
              </p>
            </CardContent>
          </Card>
        </div>
      </section>

      <Dialog open={editorOpen} onOpenChange={setEditorOpen}>
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>
              {draft.id ? "Edit business label" : "Create business label"}
            </DialogTitle>
            <DialogDescription>
              Use names that a customer or executive would immediately recognize
              when reading dashboards or remediation summaries.
            </DialogDescription>
          </DialogHeader>

          <div className="grid gap-4">
            <label className="grid gap-2">
              <span className="text-sm font-medium">Name</span>
              <Input
                value={draft.name}
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    name: event.target.value,
                  }))
                }
                placeholder="Production"
              />
            </label>

            <label className="grid gap-2">
              <span className="text-sm font-medium">Description</span>
              <Input
                value={draft.description ?? ""}
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    description: event.target.value,
                  }))
                }
                placeholder="Production-facing services and endpoints"
              />
            </label>

            <div className="grid gap-4 sm:grid-cols-[10rem_minmax(0,1fr)]">
              <label className="grid gap-2">
                <span className="text-sm font-medium">Color</span>
                <Input
                  type="color"
                  value={draft.color ?? "#2563eb"}
                  onChange={(event) =>
                    setDraft((current) => ({
                      ...current,
                      color: event.target.value,
                    }))
                  }
                  className="h-11 p-1"
                />
              </label>
              <button
                type="button"
                className="rounded-2xl border border-border/70 bg-muted/30 px-4 py-3 text-left"
                onClick={() =>
                  setDraft((current) => ({
                    ...current,
                    isActive: !current.isActive,
                  }))
                }
              >
                <p className="text-sm font-medium">
                  {draft.isActive ? "Active label" : "Inactive label"}
                </p>
                <p className="mt-1 text-sm text-muted-foreground">
                  {draft.isActive
                    ? "This label can be assigned to assets immediately."
                    : "This label stays in history but cannot be assigned to new assets."}
                </p>
              </button>
            </div>

            <div className="grid gap-2">
              <span className="text-sm font-medium">Business value category</span>
              <div className="grid gap-2 sm:grid-cols-2">
                {businessLabelWeightCategorySchema.options.map((cat) => {
                  const cfg = WEIGHT_CATEGORY_CONFIG[cat]
                  const isSelected = (draft.weightCategory ?? 'Normal') === cat
                  return (
                    <button
                      key={cat}
                      type="button"
                      className={`rounded-2xl border px-4 py-3 text-left transition-colors ${
                        isSelected
                          ? 'border-primary bg-primary/5'
                          : 'border-border/70 bg-muted/20 hover:bg-muted/40'
                      }`}
                      onClick={() =>
                        setDraft((current) => ({ ...current, weightCategory: cat }))
                      }
                    >
                      <div className="flex items-center justify-between">
                        <p className="text-sm font-medium">{cfg.label}</p>
                        <WeightMarker category={cat} />
                      </div>
                      <p className="mt-1 text-xs text-muted-foreground">{cfg.description}</p>
                    </button>
                  )
                })}
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              onClick={() => saveMutation.mutate(draft)}
              disabled={
                saveMutation.isPending || draft.name.trim().length === 0
              }
            >
              {saveMutation.isPending
                ? "Saving..."
                : draft.id
                  ? "Save changes"
                  : "Create label"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog
        open={!!deleteTarget}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
      >
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Delete business label</DialogTitle>
            <DialogDescription>
              {deleteTarget
                ? `Delete ${deleteTarget.name}? Assets using this label will lose that business context.`
                : "Delete this business label?"}
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
              disabled={!deleteTarget || deleteMutation.isPending}
              onClick={() => {
                if (deleteTarget) {
                  deleteMutation.mutate(deleteTarget.id);
                }
              }}
            >
              {deleteMutation.isPending ? "Deleting..." : "Delete"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function BusinessLabelChip({
  name,
  color,
  weightCategory,
}: {
  name: string
  color: string | null
  weightCategory: BusinessLabelWeightCategory
}) {
  const cfg = WEIGHT_CATEGORY_CONFIG[weightCategory]
  return (
    <Badge
      variant="outline"
      className="rounded-full border-border/70 bg-background/50 px-3 py-1 text-foreground"
      title={`${cfg.label} business value, ${cfg.riskWeight}x risk weight`}
    >
      <span
        className="mr-2 inline-flex size-2.5 rounded-full border border-black/10"
        style={{ backgroundColor: color ?? 'var(--muted-foreground)' }}
      />
      {name}
      <WeightMarker category={weightCategory} className="ml-2" />
    </Badge>
  )
}

function WeightMarker({
  category,
  className,
}: {
  category: BusinessLabelWeightCategory
  className?: string
}) {
  const cfg = WEIGHT_CATEGORY_CONFIG[category]
  const colorClass =
    category === 'Informational'
      ? 'text-muted-foreground'
      : category === 'Normal'
        ? 'text-muted-foreground/60'
        : category === 'Sensitive'
          ? 'text-amber-500'
          : 'text-destructive'

  if (category === 'Normal') return null

  return (
    <span
      className={`inline-flex items-center rounded-sm px-1 text-[10px] font-semibold leading-none ${colorClass} ${className ?? ''}`}
      title={`${cfg.label} business value, ${cfg.riskWeight}x risk weight`}
    >
      {cfg.riskWeight}×
    </span>
  )
}
