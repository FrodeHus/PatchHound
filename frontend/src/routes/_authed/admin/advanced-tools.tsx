import { useMemo, useState } from 'react'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Pencil, Plus, Play, Trash2 } from 'lucide-react'
import {
  createAdvancedTool,
  deleteAdvancedTool,
  fetchAdvancedTools,
  testAdvancedToolAiSummary,
  testAdvancedToolQuery,
  updateAdvancedTool,
} from '@/api/advanced-tools.functions'
import { fetchTenantAiProfiles } from '@/api/ai-settings.functions'
import type { AdvancedTool, AdvancedToolAiSummaryResult, AdvancedToolCatalog, AdvancedToolExecutionResult } from '@/api/advanced-tools.schemas'
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

export const Route = createFileRoute('/_authed/admin/advanced-tools')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  loader: () => fetchAdvancedTools({ data: {} }),
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
  const queryClient = useQueryClient()
  const [editorOpen, setEditorOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<AdvancedTool | null>(null)
  const [draft, setDraft] = useState<ToolDraft>(emptyDraft)
  const [sampleParameters, setSampleParameters] = useState<Record<string, string>>({})
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
        return
      }
      await createAdvancedTool({
        data: {
          name: value.name,
          description: value.description,
          supportedAssetTypes: value.supportedAssetTypes,
          kqlQuery: value.kqlQuery,
          aiPrompt: value.aiPrompt,
          enabled: value.enabled,
        },
      })
    },
    onSuccess: async () => {
      toast.success(draft.id ? 'Advanced tool updated' : 'Advanced tool created')
      setEditorOpen(false)
      setDraft(emptyDraft)
      setSampleParameters({})
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
      setDeleteTarget(null)
      await queryClient.invalidateQueries({ queryKey: ['advanced-tools'] })
    },
    onError: (error: Error) => {
      toast.error(error.message || 'Failed to delete advanced tool')
      setDeleteTarget(null)
    },
  })

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

  const requiredParameters = (draft.kqlQuery.match(/\{\{\s*([a-zA-Z0-9._-]+)\s*\}\}/g) ?? [])
    .map((match) => match.replace(/[{}]/g, '').trim())
    .filter((value, index, values) => values.indexOf(value) === index)
    .sort((left, right) => left.localeCompare(right))

  const canTest = draft.kqlQuery.trim().length > 0
    && requiredParameters.every((parameter) => (sampleParameters[parameter] ?? '').trim().length > 0)
  const defaultAiProfile = (profilesQuery.data ?? []).find((profile) => profile.isDefault && profile.isEnabled) ?? null
  const canTestAiSummary = canTest && !!defaultAiProfile
  const aiUnavailableReason = defaultAiProfile
    ? null
    : 'No enabled default AI profile is configured for this tenant.'

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

  const tools = toolsQuery.data?.tools ?? []
  const parameters = toolsQuery.data?.availableParameters ?? []

  return (
    <>
      <section className="space-y-5">
        <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="space-y-2">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Integrations and automation
              </p>
              <h1 className="text-3xl font-semibold tracking-[-0.04em]">
                Advanced Tools
              </h1>
              <p className="max-w-3xl text-sm text-muted-foreground">
                Manage reusable Defender KQL tools that operators can run from asset detail to investigate vulnerable components and installation evidence.
              </p>
            </div>
            <Button
              type="button"
              className="rounded-full"
              onClick={() => {
                setDraft(emptyDraft)
                setSampleParameters({})
                setEditorOpen(true)
              }}
            >
              <Plus className="mr-2 size-4" />
              New tool
            </Button>
          </div>
        </div>

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(22rem,0.65fr)]">
          <section className="space-y-4">
                {tools.map((tool: AdvancedTool) => (
              <Card key={tool.id} className="rounded-3xl border-border/70">
                <CardHeader>
                  <div className="flex items-start justify-between gap-4">
                    <div className="space-y-3">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge className="rounded-full border-primary/30 bg-primary/10 text-primary">
                          {tool.enabled ? 'Enabled' : 'Disabled'}
                        </Badge>
                        {tool.supportedAssetTypes.map((assetType: string) => (
                          <Badge
                            key={assetType}
                            variant="outline"
                            className="rounded-full border-border/70 bg-background/50"
                          >
                            {assetType}
                          </Badge>
                        ))}
                      </div>
                      <div>
                        <CardTitle>{tool.name}</CardTitle>
                        <CardDescription className="mt-2">
                          {tool.description}
                        </CardDescription>
                      </div>
                      <div className="rounded-2xl border border-border/70 bg-background/60 p-3">
                        <pre className="overflow-x-auto text-xs text-muted-foreground">
                          <code>{tool.kqlQuery}</code>
                        </pre>
                      </div>
                    </div>
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="rounded-full"
                        onClick={() => {
                          setDraft(tool)
                          setSampleParameters({})
                          setEditorOpen(true)
                        }}
                      >
                        <Pencil className="mr-2 size-4" />
                        Edit
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="rounded-full text-destructive hover:text-destructive"
                        onClick={() => setDeleteTarget(tool)}
                      >
                        <Trash2 className="mr-2 size-4" />
                        Delete
                      </Button>
                    </div>
                  </div>
                </CardHeader>
              </Card>
            ))}
          </section>

          <Card className="rounded-3xl border-border/70">
            <CardHeader>
              <CardTitle>Allowed parameters</CardTitle>
              <CardDescription>
                Use double-brace placeholders inside the KQL query. PatchHound resolves them from the selected asset and vulnerability context before running the tool.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3 text-sm text-muted-foreground">
              {parameters.map((parameter) => (
                <div key={parameter.name} className="rounded-2xl border border-border/70 bg-background/60 p-3">
                  <p className="font-medium text-foreground">{`{{${parameter.name}}}`}</p>
                  <p className="mt-1">{parameter.description}</p>
                </div>
              ))}
            </CardContent>
          </Card>
        </div>
      </section>

      <Dialog open={editorOpen} onOpenChange={setEditorOpen}>
        <DialogContent size="lg" className="sm:max-w-[70vw]">
          <DialogHeader>
            <DialogTitle>
              {draft.id ? 'Edit advanced tool' : 'Create advanced tool'}
            </DialogTitle>
          </DialogHeader>

          <div className="space-y-5">
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
              <Label>Supported asset types</Label>
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
            </div>

            <div className="space-y-3">
              <div className="flex items-center justify-between gap-3">
                <Label>KQL query</Label>
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
              <KqlEditor
                value={draft.kqlQuery}
                onChange={(value) => setDraft((current) => ({ ...current, kqlQuery: value }))}
                parameters={parameters.map((parameter) => parameter.name)}
                minHeight={280}
              />
            </div>

            <div className="space-y-3">
              <div className="space-y-1">
                <Label>Optional AI prompt</Label>
                <p className="text-sm text-muted-foreground">
                  Guides the AI summary after the KQL query runs. Leave it blank to use the default operational summary prompt.
                </p>
              </div>
              <Textarea
                value={draft.aiPrompt}
                onChange={(event) => setDraft((current) => ({ ...current, aiPrompt: event.target.value }))}
                placeholder="Summarize what the KQL results prove, call out the strongest evidence of installation or bundled-component presence, and explain the next operational conclusion."
              />
            </div>

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

            <div className="space-y-3 rounded-3xl border border-border/70 bg-background/60 p-4">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <h3 className="text-sm font-medium">Test query</h3>
                  <p className="text-sm text-muted-foreground">
                    Run the rendered query against the current tenant’s Defender advanced hunting endpoint.
                  </p>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  className="rounded-full"
                  onClick={() => testMutation.mutate()}
                  disabled={!canTest || testMutation.isPending}
                >
                  {testMutation.isPending ? <Play className="mr-2 size-4 animate-pulse" /> : <Play className="mr-2 size-4" />}
                  Test query
                </Button>
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

              {testMutation.data ? (
                <div className="space-y-4">
                  <div className="rounded-2xl border border-border/70 bg-card/70 p-3">
                    <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                      Rendered query
                    </p>
                    <pre className="mt-2 overflow-x-auto text-xs text-muted-foreground">
                      <code>{testMutation.data?.renderedQuery ?? ''}</code>
                    </pre>
                  </div>
                  <DataTable
                    columns={columns}
                    data={testMutation.data?.results ?? []}
                    emptyState={<span className="text-sm text-muted-foreground">The query returned no rows.</span>}
                  />
                </div>
              ) : null}

              {aiSummaryMutation.data ? (
                <div className="space-y-4 rounded-2xl border border-border/70 bg-card/70 p-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge>{aiSummaryMutation.data.profileName}</Badge>
                    <Badge variant="outline">{aiSummaryMutation.data.providerType}</Badge>
                    <Badge variant="outline">{aiSummaryMutation.data.model}</Badge>
                  </div>
                  <div className="rounded-2xl border border-border/70 bg-background/60 p-3">
                    <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                      Rendered query
                    </p>
                    <pre className="mt-2 overflow-x-auto text-xs text-muted-foreground">
                      <code>{aiSummaryMutation.data.renderedQuery}</code>
                    </pre>
                  </div>
                  <div>
                    <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                      AI summary
                    </p>
                    <MarkdownViewer content={aiSummaryMutation.data.content} className="mt-3" />
                  </div>
                </div>
              ) : null}
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setEditorOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              onClick={() => saveMutation.mutate(draft)}
              disabled={saveMutation.isPending || draft.name.trim().length === 0 || draft.kqlQuery.trim().length === 0 || draft.supportedAssetTypes.length === 0}
            >
              {draft.id ? 'Save changes' : 'Create tool'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={deleteTarget !== null} onOpenChange={(open) => !open && setDeleteTarget(null)}>
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Delete advanced tool</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Delete {deleteTarget?.name}? This removes it from the admin catalog and from Defender-backed asset detail views.
          </p>
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
    </>
  )
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
