import { useMemo, useState } from 'react'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { toast } from 'sonner'
import { Plus, Play, Trash2 } from 'lucide-react'
import {
  createAdvancedTool,
  deleteAdvancedTool,
  fetchAdvancedTools,
  testAdvancedToolAiSummary,
  testAdvancedToolQuery,
  updateAdvancedTool,
} from '@/api/advanced-tools.functions'
import { fetchTenantAiProfiles } from '@/api/ai-settings.functions'
import type {
  AdvancedTool,
  AdvancedToolAiSummaryResult,
  AdvancedToolCatalog,
  AdvancedToolExecutionResult,
} from '@/api/advanced-tools.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { KqlEditor } from '@/components/ui/kql-editor'
import { Label } from '@/components/ui/label'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import { Textarea } from '@/components/ui/textarea'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

export const Route = createFileRoute('/_authed/admin/platform/advanced-tools')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  loader: () => fetchAdvancedTools({ data: {} }),
  validateSearch: z.object({
    toolId: z.string().uuid().optional(),
    mode: z.enum(['new']).optional(),
  }),
  component: AdvancedToolsPage,
})

type ToolDraft = {
  id?: string
  name: string
  description: string
  supportedAssetTypes: string[]
  kqlQuery: string
  aiPrompt: string
  enabled: boolean
}

const emptyDraft: ToolDraft = {
  name: '',
  description: '',
  supportedAssetTypes: ['Device'],
  kqlQuery: '',
  aiPrompt: '',
  enabled: true,
}

function AdvancedToolsPage() {
  const initialCatalog = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const queryClient = useQueryClient()
  const [deleteTarget, setDeleteTarget] = useState<AdvancedTool | null>(null)

  const profilesQuery = useQuery({
    queryKey: ['tenant-ai-profiles'],
    queryFn: () => fetchTenantAiProfiles(),
    staleTime: 30_000,
  })

  const toolsQuery = useQuery({
    queryKey: ['advanced-tools'],
    queryFn: () => fetchAdvancedTools({ data: {} }),
    initialData: initialCatalog,
    staleTime: 30_000,
  }) as { data: AdvancedToolCatalog | undefined }

  const saveMutation = useMutation({
    mutationFn: async (value: ToolDraft) => {
      if (value.id) {
        await updateAdvancedTool({
          data: {
            id: value.id,
            name: value.name,
            description: value.description,
            supportedAssetTypes: value.supportedAssetTypes,
            kqlQuery: value.kqlQuery,
            aiPrompt: value.aiPrompt,
            enabled: value.enabled,
          },
        })
        return value.id
      }

      const created = await createAdvancedTool({
        data: {
          name: value.name,
          description: value.description,
          supportedAssetTypes: value.supportedAssetTypes,
          kqlQuery: value.kqlQuery,
          aiPrompt: value.aiPrompt,
          enabled: value.enabled,
        },
      })

      return created.id
    },
    onSuccess: async (_, value) => {
      toast.success(value.id ? 'Advanced tool updated' : 'Advanced tool created')
      await queryClient.invalidateQueries({ queryKey: ['advanced-tools'] })
    },
    onError: (error: Error) => {
      toast.error(error.message || 'Failed to save advanced tool')
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async (toolId: string) => {
      await deleteAdvancedTool({ data: { id: toolId } })
    },
    onSuccess: async () => {
      toast.success('Advanced tool deleted')
      if (deleteTarget && search.toolId === deleteTarget.id) {
        await navigate({ search: {} })
      }
      setDeleteTarget(null)
      await queryClient.invalidateQueries({ queryKey: ['advanced-tools'] })
    },
    onError: (error: Error) => {
      toast.error(error.message || 'Failed to delete advanced tool')
      setDeleteTarget(null)
    },
  })

  const defaultAiProfile = (profilesQuery.data ?? []).find((profile) => profile.isDefault && profile.isEnabled) ?? null
  const aiUnavailableReason = defaultAiProfile
    ? null
    : 'No enabled default AI profile is configured for this tenant.'

  const tools = useMemo(() => toolsQuery.data?.tools ?? [], [toolsQuery.data?.tools])
  const parameters = toolsQuery.data?.availableParameters ?? []
  const selectedTool = useMemo(
    () => tools.find((tool) => tool.id === search.toolId) ?? null,
    [search.toolId, tools],
  )
  const workbenchKey = search.mode === 'new' ? 'new' : selectedTool?.id ?? 'blank'
  const workbenchSeed = search.mode === 'new'
    ? emptyDraft
    : selectedTool
      ? {
          id: selectedTool.id,
          name: selectedTool.name,
          description: selectedTool.description,
          supportedAssetTypes: selectedTool.supportedAssetTypes,
          kqlQuery: selectedTool.kqlQuery,
          aiPrompt: selectedTool.aiPrompt,
          enabled: selectedTool.enabled,
        }
      : emptyDraft

  return (
    <>
      <section className="space-y-5">
        <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Platform configuration
            </p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">
              Advanced Tools
            </h1>
            <p className="max-w-3xl text-sm text-muted-foreground">
              Create and test reusable Defender KQL tools that can be surfaced
              in asset details views and used to power AI-assisted
              investigations. Define the KQL query, specify required parameters,
              and optionally add an AI prompt to guide natural language
              summaries of the results.
            </p>
          </div>
        </div>

        <div className="grid gap-5 xl:grid-cols-[18rem_minmax(0,1fr)]">
          <aside className="space-y-4">
            <Card className="rounded-3xl border-border/70">
              <CardHeader className="space-y-3">
                <div>
                  <CardTitle>Tool catalog</CardTitle>
                  <CardDescription>
                    Choose a saved tool or start a new draft.
                  </CardDescription>
                </div>
                <Button
                  type="button"
                  className="w-full rounded-full"
                  onClick={() => void navigate({ search: { mode: "new" } })}
                >
                  <Plus className="mr-2 size-4" />
                  New tool
                </Button>
              </CardHeader>
              <CardContent className="space-y-2">
                {tools.map((tool: AdvancedTool) => {
                  const active =
                    search.toolId === tool.id && search.mode !== "new";

                  return (
                    <div
                      key={tool.id}
                      className="flex min-w-0 items-center gap-2"
                    >
                      <button
                        type="button"
                        className={`min-w-0 flex-1 rounded-2xl border px-3 py-2 text-left text-sm transition ${
                          active
                            ? "border-primary/40 bg-primary/10 text-foreground"
                            : "border-border/70 bg-background/60 text-foreground hover:border-foreground/20 hover:bg-muted/20"
                        }`}
                        onClick={() =>
                          void navigate({ search: { toolId: tool.id } })
                        }
                      >
                        <div className="truncate font-medium">{tool.name}</div>
                      </button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon-sm"
                        className="rounded-full text-destructive hover:text-destructive"
                        onClick={() => setDeleteTarget(tool)}
                      >
                        <Trash2 className="size-4" />
                        <span className="sr-only">Delete {tool.name}</span>
                      </Button>
                    </div>
                  );
                })}
              </CardContent>
            </Card>
          </aside>

          <section className="space-y-5">
            <AdvancedToolWorkbench
              key={workbenchKey}
              initialDraft={workbenchSeed}
              parameters={parameters}
              defaultAiProfile={defaultAiProfile}
              aiUnavailableReason={aiUnavailableReason}
              onClearDraft={() => void navigate({ search: {} })}
              onSave={async (value) => {
                const toolId = await saveMutation.mutateAsync(value);
                await navigate({ search: toolId ? { toolId } : {} });
              }}
              isSaving={saveMutation.isPending}
            />
          </section>
        </div>
      </section>

      <Dialog
        open={deleteTarget !== null}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
      >
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Delete advanced tool</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Delete {deleteTarget?.name}? This removes it from the admin catalog
            and from Defender-backed asset detail views.
          </p>
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
    </>
  );
}

function formatCellValue(value: unknown) {
  if (value === null || value === undefined) {
    return ''
  }

  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }

  return JSON.stringify(value)
}

type AdvancedToolWorkbenchProps = {
  initialDraft: ToolDraft
  parameters: Array<{ name: string; description: string }>
  defaultAiProfile: { isDefault: boolean; isEnabled: boolean } | null
  aiUnavailableReason: string | null
  isSaving: boolean
  onSave: (value: ToolDraft) => Promise<void>
  onClearDraft: () => void
}

function AdvancedToolWorkbench({
  initialDraft,
  parameters,
  defaultAiProfile,
  aiUnavailableReason,
  isSaving,
  onSave,
  onClearDraft,
}: AdvancedToolWorkbenchProps) {
  const [draft, setDraft] = useState<ToolDraft>(initialDraft)
  const [sampleParameters, setSampleParameters] = useState<Record<string, string>>({})
  const [currentStep, setCurrentStep] = useState<1 | 2>(1)

  const requiredParameters = (draft.kqlQuery.match(/\{\{\s*([a-zA-Z0-9._-]+)\s*\}\}/g) ?? [])
    .map((match) => match.replace(/[{}]/g, '').trim())
    .filter((value, index, values) => values.indexOf(value) === index)
    .sort((left, right) => left.localeCompare(right))

  const canTest = draft.kqlQuery.trim().length > 0
    && requiredParameters.every((parameter) => (sampleParameters[parameter] ?? '').trim().length > 0)
  const canTestAiSummary = canTest && !!defaultAiProfile

  const testMutation = useMutation<AdvancedToolExecutionResult, Error>({
    mutationFn: async () =>
      testAdvancedToolQuery({
        data: {
          kqlQuery: draft.kqlQuery,
          sampleParameters: requiredParameters.reduce<Record<string, string | null>>((acc, key) => {
            acc[key] = sampleParameters[key] ?? ''
            return acc
          }, {}),
        },
      }),
    onError: (error: Error) => {
      toast.error(error.message || 'Failed to run test query')
    },
  })

  const aiSummaryMutation = useMutation<AdvancedToolAiSummaryResult, Error>({
    mutationFn: async () =>
      testAdvancedToolAiSummary({
        data: {
          kqlQuery: draft.kqlQuery,
          aiPrompt: draft.aiPrompt,
          sampleParameters: requiredParameters.reduce<Record<string, string | null>>((acc, key) => {
            acc[key] = sampleParameters[key] ?? ''
            return acc
          }, {}),
        },
      }),
    onError: (error: Error) => {
      toast.error(error.message || 'Failed to generate AI summary')
    },
  })

  const columns = useMemo(() => {
    const schema = testMutation.data?.schema ?? []
    return schema.map((column) => ({
      accessorKey: column.name,
      header: column.name,
      cell: ({ row }: { row: { original: Record<string, unknown> } }) => {
        const value = row.original[column.name]
        return <span className="text-xs">{formatCellValue(value)}</span>
      },
    }))
  }, [testMutation.data?.schema])

  const canSave = !isSaving
    && draft.name.trim().length > 0
    && draft.kqlQuery.trim().length > 0
    && draft.supportedAssetTypes.length > 0

  async function handleSaveCurrentStage() {
    await onSave(draft)
  }

  return (
    <Card className="rounded-[32px] border-border/70">
      <CardHeader className="space-y-2">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Tool workbench
            </p>
            <CardTitle className="text-2xl tracking-[-0.03em]">
              {draft.id ? 'Edit advanced tool' : 'Create advanced tool'}
            </CardTitle>
            <CardDescription className="max-w-3xl">
              Define the KQL query, optional AI prompt, and test both raw query results and AI interpretation directly in this workspace.
            </CardDescription>
          </div>
          {draft.id ? (
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
              Editing saved tool
            </Badge>
          ) : (
            <Badge className="rounded-full border-primary/30 bg-primary/10 text-primary">
              Unsaved draft
            </Badge>
          )}
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            className={`rounded-full border px-3 py-1.5 text-sm transition ${currentStep === 1 ? 'border-primary/35 bg-primary/10 text-foreground' : 'border-border/70 bg-background/50 text-muted-foreground hover:text-foreground'}`}
            onClick={() => setCurrentStep(1)}
          >
            1. Query
          </button>
          <button
            type="button"
            className={`rounded-full border px-3 py-1.5 text-sm transition ${currentStep === 2 ? 'border-primary/35 bg-primary/10 text-foreground' : 'border-border/70 bg-background/50 text-muted-foreground hover:text-foreground'}`}
            onClick={() => setCurrentStep(2)}
          >
            2. AI prompt
          </button>
        </div>

        {currentStep === 1 ? (
          <>
            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label>Name</Label>
                <Input
                  value={draft.name}
                  onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))}
                  placeholder="Component evidence lookup"
                />
              </div>
              <div className="space-y-2">
                <Label>Description</Label>
                <Input
                  value={draft.description}
                  onChange={(event) => setDraft((current) => ({ ...current, description: event.target.value }))}
                  placeholder="Explain what the tool is for"
                />
              </div>
            </div>

            <div className="space-y-3">
              <div className="flex items-center justify-between gap-3">
                <Label>Supported asset types</Label>
                <label className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Checkbox
                    checked={draft.enabled}
                    onCheckedChange={(checked) =>
                      setDraft((current) => ({ ...current, enabled: Boolean(checked) }))
                    }
                  />
                  Enabled
                </label>
              </div>
              <Tooltip>
                <TooltipTrigger
                  render={
                    <label className="flex items-center gap-3 rounded-2xl border border-border/70 bg-background/60 p-3 text-sm">
                      <Checkbox
                        checked={draft.supportedAssetTypes.includes('Device')}
                        onCheckedChange={(checked) => {
                          setDraft((current) => ({
                            ...current,
                            supportedAssetTypes: checked ? ['Device'] : [],
                          }))
                        }}
                      />
                      <span>Device</span>
                    </label>
                  }
                />
                <TooltipContent side="top" className="max-w-sm space-y-2">
                  <p className="text-xs font-semibold uppercase tracking-[0.18em]">Allowed parameters</p>
                  <ul className="space-y-1 text-sm">
                    {parameters.map((parameter) => (
                      <li key={parameter.name}>
                        <span className="font-medium">{`{{${parameter.name}}}`}</span>
                        {' '}
                        {parameter.description}
                      </li>
                    ))}
                  </ul>
                </TooltipContent>
              </Tooltip>
            </div>

            <div className="space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="space-y-1">
                  <Label>KQL query</Label>
                  <p className="text-sm text-muted-foreground">
                    Define and test the Defender query first. Press <span className="font-medium text-foreground">Shift+Enter</span> to run it from the editor.
                  </p>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="rounded-full"
                  onClick={() => testMutation.mutate()}
                  disabled={!canTest || testMutation.isPending}
                >
                  {testMutation.isPending ? <Play className="mr-2 size-4 animate-pulse" /> : <Play className="mr-2 size-4" />}
                  Test query
                </Button>
              </div>
              <KqlEditor
                value={draft.kqlQuery}
                onChange={(value) => setDraft((current) => ({ ...current, kqlQuery: value }))}
                parameters={parameters.map((parameter) => parameter.name)}
                minHeight={340}
                onShiftEnter={() => {
                  if (canTest && !testMutation.isPending) {
                    testMutation.mutate()
                  }
                }}
              />
            </div>
          </>
        ) : (
          <div className="space-y-3">
            <div className="space-y-1">
              <Label>Optional AI prompt</Label>
              <p className="text-sm text-muted-foreground">
                Add an optional prompt to shape the AI summary after the KQL query runs. This step is optional.
              </p>
            </div>
            <Textarea
              value={draft.aiPrompt}
              onChange={(event) => setDraft((current) => ({ ...current, aiPrompt: event.target.value }))}
              placeholder="Summarize what the KQL results prove, call out the strongest evidence of installation or bundled-component presence, and explain the next operational conclusion."
            />
          </div>
        )}

        {requiredParameters.length > 0 ? (
          <div className="space-y-3 rounded-3xl border border-border/70 bg-muted/20 p-4">
            <div className="space-y-1">
              <h3 className="text-sm font-medium">Sample parameters</h3>
              <p className="text-sm text-muted-foreground">
                This query uses placeholders. Provide sample values before testing it against Defender.
              </p>
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              {requiredParameters.map((parameter) => (
                <div key={parameter} className="space-y-2">
                  <Label>{`{{${parameter}}}`}</Label>
                  <Input
                    value={sampleParameters[parameter] ?? ''}
                    onChange={(event) =>
                      setSampleParameters((current) => ({
                        ...current,
                        [parameter]: event.target.value,
                      }))
                    }
                  />
                </div>
              ))}
            </div>
          </div>
        ) : null}

        <div className="space-y-4 border-t border-border/70 pt-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="space-y-1">
              <h3 className="text-sm font-medium">
                {currentStep === 1 ? 'Query test workbench' : 'AI test workbench'}
              </h3>
              <p className="text-sm text-muted-foreground">
                {currentStep === 1
                  ? 'Run the query against Defender and inspect the raw result grid before saving this stage.'
                  : 'Test the optional AI prompt against the current query results before saving the AI stage.'}
              </p>
            </div>
            {currentStep === 2 ? (
              <div className="flex flex-wrap items-center gap-2">
                <Tooltip>
                  <TooltipTrigger>
                    <span>
                      <Button
                        type="button"
                        variant="outline"
                        className="rounded-full"
                        onClick={() => aiSummaryMutation.mutate()}
                        disabled={!canTestAiSummary || aiSummaryMutation.isPending}
                      >
                        {aiSummaryMutation.isPending ? <Play className="mr-2 size-4 animate-pulse" /> : <Play className="mr-2 size-4" />}
                        Test AI summary
                      </Button>
                    </span>
                  </TooltipTrigger>
                  {!canTestAiSummary && aiUnavailableReason ? (
                    <TooltipContent>{aiUnavailableReason}</TooltipContent>
                  ) : null}
                </Tooltip>
              </div>
            ) : null}
          </div>

          {testMutation.data && currentStep === 1 ? (
            <div className="space-y-3">
              <div className="space-y-1">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  Query results
                </p>
                <p className="text-sm text-muted-foreground">
                  {testMutation.data.results.length} rows returned from Defender advanced hunting.
                </p>
              </div>
              <div className="max-h-[28rem] overflow-auto rounded-2xl border border-border/70">
                <DataTable
                  columns={columns}
                  data={testMutation.data.results}
                  className="min-w-max"
                  emptyState={<span className="text-sm text-muted-foreground">The query returned no rows.</span>}
                />
              </div>
            </div>
          ) : null}

          {aiSummaryMutation.data && currentStep === 2 ? (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center gap-2">
                <Badge>{aiSummaryMutation.data.profileName}</Badge>
                <Badge variant="outline">{aiSummaryMutation.data.providerType}</Badge>
                <Badge variant="outline">{aiSummaryMutation.data.model}</Badge>
              </div>
              <div className="space-y-1">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  AI summary
                </p>
                <div className="max-h-[20rem] overflow-auto rounded-2xl border border-border/70 p-4">
                  <MarkdownViewer content={aiSummaryMutation.data.content} />
                </div>
              </div>
            </div>
          ) : null}
        </div>

        <div className="flex flex-wrap justify-end gap-2 border-t border-border/70 pt-4">
          <Button
            type="button"
            variant="outline"
            onClick={onClearDraft}
          >
            Clear draft
          </Button>
          {currentStep === 2 ? (
            <Button
              type="button"
              variant="outline"
              onClick={() => setCurrentStep(1)}
            >
              Previous: query
            </Button>
          ) : (
            <Button
              type="button"
              variant="outline"
              onClick={() => setCurrentStep(2)}
            >
              Next: AI prompt
            </Button>
          )}
          <Button
            type="button"
            onClick={() => void handleSaveCurrentStage()}
            disabled={!canSave}
          >
            {currentStep === 1
              ? draft.id
                ? 'Save query stage'
                : 'Save and continue'
              : draft.id
                ? 'Save AI stage'
                : 'Create tool'}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
