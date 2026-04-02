import { useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { Braces, ChevronDown, ChevronUp, Expand, Play } from 'lucide-react'
import { fetchAdvancedTools, runAdvancedToolForAsset } from '@/api/advanced-tools.functions'
import type { AdvancedToolAssetExecutionResult, AdvancedToolCatalog } from '@/api/advanced-tools.schemas'
import type { AssetDetail } from '@/api/assets.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

type Props = {
  asset: AssetDetail
}

export function AssetAdvancedToolsPanel({ asset }: Props) {
  const [selectedToolId, setSelectedToolId] = useState<string>('')
  const [useAllOpenVulnerabilities, setUseAllOpenVulnerabilities] = useState(true)
  const [selectedVulnerabilityIds, setSelectedVulnerabilityIds] = useState<string[]>([])
  const [expandedVulnerabilityIds, setExpandedVulnerabilityIds] = useState<string[]>([])
  const [rawResultsOpen, setRawResultsOpen] = useState(false)
  const [reportOpen, setReportOpen] = useState(false)

  const toolsQuery = useQuery({
    queryKey: ['advanced-tools', 'asset', asset.id],
    queryFn: () => fetchAdvancedTools({ data: { assetType: asset.assetType } }),
    staleTime: 60_000,
  }) as { data: AdvancedToolCatalog | undefined }

  const runMutation = useMutation<AdvancedToolAssetExecutionResult, Error>({
    mutationFn: async () =>
      runAdvancedToolForAsset({
        data: {
          assetId: asset.id,
          toolId: selectedToolId,
          useAllOpenVulnerabilities,
          vulnerabilityIds: useAllOpenVulnerabilities ? [] : selectedVulnerabilityIds,
        },
      }),
    onError: (error: Error) => {
      toast.error(error.message || 'Failed to run advanced tool')
    },
  })

  const tools = toolsQuery.data?.tools ?? []
  const selectedTool = tools.find((tool) => tool.id === selectedToolId) ?? null
  const needsVulnerabilityContext = /\{\{\s*vuln\./.test(selectedTool?.kqlQuery ?? '')
  const selectableVulnerabilities = useMemo(
    () =>
      [...asset.vulnerabilities]
        .sort((left, right) => {
          const severityDelta
            = severityRank(right.effectiveSeverity) - severityRank(left.effectiveSeverity)
          if (severityDelta !== 0) {
            return severityDelta
          }

          const scoreDelta = (right.effectiveScore ?? right.vendorScore ?? 0)
            - (left.effectiveScore ?? left.vendorScore ?? 0)
          if (scoreDelta !== 0) {
            return scoreDelta
          }

          return left.externalId.localeCompare(right.externalId)
        }),
    [asset.vulnerabilities],
  )

  if (asset.assetType !== 'Device') {
    return null
  }

  return (
    <Card className="rounded-3xl border-border/70 bg-card/94">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <span className="flex size-10 items-center justify-center rounded-2xl border border-border/70 bg-background/60 text-primary">
                <Braces className="size-4" />
              </span>
              <div>
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  Advanced tools
                </p>
                <CardTitle>Run Defender KQL tools</CardTitle>
              </div>
            </div>
            <CardDescription>
              Run investigation tools using Defender advanced hunting.
            </CardDescription>
          </div>
          <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
            Defender-backed device
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_auto]">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Tool
            </p>
            <Select value={selectedToolId} onValueChange={(value) => setSelectedToolId(value ?? '')}>
              <SelectTrigger className="h-11 w-full rounded-2xl px-3">
                <SelectValue placeholder="Select an advanced tool">
                  {selectedTool?.name ?? 'Select an advanced tool'}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {tools.map((tool) => (
                  <SelectItem key={tool.id} value={tool.id}>
                    {tool.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {selectedTool ? (
              <p className="text-sm text-muted-foreground">{selectedTool.description}</p>
            ) : null}
          </div>
          <div className="flex items-end">
            <div className="flex flex-wrap items-center justify-end gap-2">
              {runMutation.data ? (
                <>
                  <Button type="button" variant="outline" className="rounded-full" onClick={() => setRawResultsOpen(true)}>
                    View raw results
                  </Button>
                  {runMutation.data.report ? (
                    <Button type="button" variant="outline" className="rounded-full" onClick={() => setReportOpen(true)}>
                      <Expand className="mr-2 size-4" />
                      Open report
                    </Button>
                  ) : null}
                </>
              ) : null}
              <Button
                type="button"
                className="rounded-full"
                disabled={
                  !selectedToolId
                  || runMutation.isPending
                  || (needsVulnerabilityContext
                    && !useAllOpenVulnerabilities
                    && selectedVulnerabilityIds.length === 0)
                }
                onClick={() => runMutation.mutate()}
              >
                <Play className="mr-2 size-4" />
                Run tool
              </Button>
            </div>
          </div>
        </div>

        {needsVulnerabilityContext ? (
          <div className="space-y-3 rounded-2xl border border-border/70 bg-background/60 p-4">
            <div className="space-y-1">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Vulnerability context
              </p>
              <p className="text-sm text-muted-foreground">
                This tool uses vulnerability placeholders. Run it for all open vulnerabilities or choose specific ones.
              </p>
            </div>
            <label className="flex items-center gap-3 text-sm">
              <Checkbox
                checked={useAllOpenVulnerabilities}
                onCheckedChange={(checked) => {
                  const nextValue = Boolean(checked)
                  setUseAllOpenVulnerabilities(nextValue)
                  if (nextValue) {
                    setSelectedVulnerabilityIds([])
                  }
                }}
              />
              <span>Use all open vulnerabilities</span>
            </label>
            {!useAllOpenVulnerabilities ? (
              <div className="max-h-[24rem] space-y-2 overflow-auto pr-1">
                {selectableVulnerabilities.map((vulnerability) => (
                  <div
                    key={vulnerability.vulnerabilityId}
                    className="rounded-2xl border border-border/70 bg-card/80"
                  >
                    <div className="flex items-center gap-3 px-3 py-3 text-sm">
                      <Checkbox
                        className="size-5 rounded-md border-2 border-border bg-background shadow-xs"
                        checked={selectedVulnerabilityIds.includes(vulnerability.vulnerabilityId)}
                        onCheckedChange={(checked) => {
                          setSelectedVulnerabilityIds((current) =>
                            checked
                              ? [...current, vulnerability.vulnerabilityId]
                              : current.filter((id) => id !== vulnerability.vulnerabilityId),
                          )
                        }}
                      />
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-medium">{vulnerability.externalId}</span>
                          <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
                            {vulnerability.effectiveSeverity}
                          </Badge>
                        </div>
                      </div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-8 rounded-full px-2"
                        aria-expanded={expandedVulnerabilityIds.includes(vulnerability.vulnerabilityId)}
                        onClick={() => {
                          setExpandedVulnerabilityIds((current) =>
                            current.includes(vulnerability.vulnerabilityId)
                              ? current.filter((id) => id !== vulnerability.vulnerabilityId)
                              : [...current, vulnerability.vulnerabilityId],
                          )
                        }}
                      >
                        {expandedVulnerabilityIds.includes(vulnerability.vulnerabilityId) ? (
                          <ChevronUp className="size-4" />
                        ) : (
                          <ChevronDown className="size-4" />
                        )}
                      </Button>
                    </div>
                    {expandedVulnerabilityIds.includes(vulnerability.vulnerabilityId) ? (
                      <div className="space-y-2 border-t border-border/60 px-4 py-3 text-sm">
                        <p className="font-medium">{vulnerability.title}</p>
                        <p className="text-muted-foreground">{vulnerability.description}</p>
                      </div>
                    ) : null}
                  </div>
                ))}
              </div>
            ) : null}
          </div>
        ) : null}

        {runMutation.data ? (
          <AdvancedToolRunSummary
            result={runMutation.data}
            onOpenRawResults={() => setRawResultsOpen(true)}
            onOpenReport={() => setReportOpen(true)}
          />
        ) : null}
      </CardContent>

      <Dialog open={rawResultsOpen} onOpenChange={setRawResultsOpen}>
        <DialogContent
          size="lg"
          className="max-h-[90vh] overflow-hidden sm:h-[80vh] sm:max-w-[80vw] sm:w-[80vw]"
        >
          <DialogHeader>
            <DialogTitle>Raw results</DialogTitle>
          </DialogHeader>
          {runMutation.data ? (
            <RawResultsDialogContent result={runMutation.data} />
          ) : null}
        </DialogContent>
      </Dialog>

      <Dialog open={reportOpen} onOpenChange={setReportOpen}>
        <DialogContent
          size="lg"
          className="max-h-[90vh] overflow-hidden sm:h-[80vh] sm:max-w-[80vw] sm:w-[80vw]"
        >
          <DialogHeader>
            <DialogTitle>Investigation report</DialogTitle>
          </DialogHeader>
          {runMutation.data ? (
            <AdvancedToolReportDialogContent result={runMutation.data} />
          ) : null}
        </DialogContent>
      </Dialog>
    </Card>
  )
}

function severityRank(severity: string) {
  switch (severity.toLowerCase()) {
    case 'critical':
      return 4
    case 'high':
      return 3
    case 'medium':
      return 2
    case 'low':
      return 1
    default:
      return 0
  }
}

function AdvancedToolRunSummary({
  result,
  onOpenRawResults,
  onOpenReport,
}: {
  result: AdvancedToolAssetExecutionResult
  onOpenRawResults: () => void
  onOpenReport: () => void
}) {
  return (
    <div className="space-y-4 rounded-2xl border border-border/70 bg-background/60 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-1">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
            Tool run ready
          </p>
          <p className="text-sm text-muted-foreground">
            {result.rawResults.rowCount} merged rows from the selected tool run.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            variant="outline"
            className="rounded-full"
            onClick={onOpenRawResults}
          >
            View raw results
          </Button>
          {result.report ? (
            <Button type="button" variant="outline" className="rounded-full" onClick={onOpenReport}>
              <Expand className="mr-2 size-4" />
              Open report
            </Button>
          ) : null}
        </div>
      </div>
      <p className="text-sm text-muted-foreground">
        {result.report
          ? 'The AI investigation report is ready. Open it in the larger reading view.'
          : result.aiUnavailableMessage ?? 'AI report is not available for this tool run.'}
      </p>
    </div>
  )
}

function AdvancedToolReportDialogContent({ result }: { result: AdvancedToolAssetExecutionResult }) {
  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
      <AdvancedToolReportBody result={result} maxHeightClassName="max-h-none" />
    </div>
  )
}

function AdvancedToolReportBody({
  result,
  maxHeightClassName,
}: {
  result: AdvancedToolAssetExecutionResult
  maxHeightClassName: string
}) {
  const report = result.report

  if (!report) {
    return (
      <div className="py-1">
        <p className="text-sm text-muted-foreground">
          {result.aiUnavailableMessage ?? 'AI report is not available for this tool run.'}
        </p>
      </div>
    )
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <Badge>{report.profileName}</Badge>
        <Badge variant="outline">{report.providerType}</Badge>
        <Badge variant="outline">{report.model}</Badge>
      </div>
      <div className={`min-h-0 overflow-auto pr-2 ${maxHeightClassName}`}>
        <MarkdownViewer content={report.content} className="prose-headings:mt-0 prose-p:mt-3" />
      </div>
    </div>
  )
}

function RawResultsDialogContent({ result }: { result: AdvancedToolAssetExecutionResult }) {
  const columns: ColumnDef<Record<string, unknown>>[] = result.rawResults.schema.map((column) => ({
    accessorKey: column.name,
    header: column.name,
    cell: ({ row }) => <span className="text-xs">{formatCellValue(row.original[column.name])}</span>,
  }))

  return (
    <div className="flex min-h-0 flex-1 flex-col space-y-4 overflow-hidden">
      <p className="text-sm text-muted-foreground">
        {result.rawResults.rowCount} merged rows returned from the tool run.
      </p>
      <div className="min-h-0 flex-1 overflow-hidden rounded-2xl border border-border/70 bg-card/70">
        <div className="h-full overflow-auto">
          <DataTable
            columns={columns}
            data={result.rawResults.rows as Record<string, unknown>[]}
            className="min-w-max"
            emptyState={<span className="text-sm text-muted-foreground">The query returned no rows.</span>}
          />
        </div>
      </div>
    </div>
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
