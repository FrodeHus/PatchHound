import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { Braces, ChevronDown, ChevronRight, Expand, Play } from 'lucide-react'
import { fetchAdvancedTools, runAdvancedToolForAsset } from '@/api/advanced-tools.functions'
import type { AdvancedToolAssetExecutionResult, AdvancedToolCatalog } from '@/api/advanced-tools.schemas'
import type { DeviceDetail, DeviceExposure } from '@/api/devices.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

type Props = {
  device: DeviceDetail
  exposures: DeviceExposure[]
}

export function DeviceAdvancedToolsPanel({ device, exposures }: Props) {
  const [selectedToolId, setSelectedToolId] = useState<string>('')
  const [useAllVulnerabilities, setUseAllVulnerabilities] = useState(true)
  const [selectedVulnerabilityIds, setSelectedVulnerabilityIds] = useState<string[]>([])
  const [vulnerabilityListExpanded, setVulnerabilityListExpanded] = useState(false)
  const [rawResultsOpen, setRawResultsOpen] = useState(false)
  const [reportOpen, setReportOpen] = useState(false)

  const openExposures = exposures.filter((e) => e.status !== 'Resolved')

  const toolsQuery = useQuery({
    queryKey: ['advanced-tools', 'device', device.id],
    queryFn: () => fetchAdvancedTools({ data: { assetType: 'Device' } }),
    staleTime: 60_000,
  }) as { data: AdvancedToolCatalog | undefined }

  const runMutation = useMutation<AdvancedToolAssetExecutionResult, Error>({
    mutationFn: async () =>
      runAdvancedToolForAsset({
        data: {
          assetId: device.id,
          toolId: selectedToolId,
          useAllOpenVulnerabilities: useAllVulnerabilities,
          vulnerabilityIds: useAllVulnerabilities ? [] : selectedVulnerabilityIds,
        },
      }),
    onError: (error: Error) => {
      toast.error(error.message || 'Failed to run advanced tool')
    },
  })

  const tools = toolsQuery.data?.tools ?? []
  const selectedTool = tools.find((tool) => tool.id === selectedToolId) ?? null

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
                disabled={!selectedToolId || runMutation.isPending || (!useAllVulnerabilities && selectedVulnerabilityIds.length === 0)}
                onClick={() => runMutation.mutate()}
              >
                <Play className="mr-2 size-4" />
                Run tool
              </Button>
            </div>
          </div>
        </div>

        {selectedToolId ? (
          <div className="space-y-3 rounded-2xl border border-border/70 bg-background/60 p-4">
            <div className="flex items-center gap-3">
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={useAllVulnerabilities}
                  onCheckedChange={(checked) => {
                    setUseAllVulnerabilities(checked === true)
                    if (checked) {
                      setSelectedVulnerabilityIds([])
                      setVulnerabilityListExpanded(false)
                    }
                  }}
                />
                All vulnerabilities
              </label>
              <span className="text-xs text-muted-foreground">
                {useAllVulnerabilities
                  ? `Targets all ${openExposures.length} open vulnerabilities`
                  : `${selectedVulnerabilityIds.length} of ${openExposures.length} selected`}
              </span>
            </div>

            {!useAllVulnerabilities ? (
              <div className="space-y-2">
                <button
                  type="button"
                  className="flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
                  onClick={() => setVulnerabilityListExpanded((v) => !v)}
                >
                  {vulnerabilityListExpanded ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
                  Select vulnerabilities
                </button>
                {vulnerabilityListExpanded ? (
                  <div className="max-h-64 space-y-1 overflow-y-auto rounded-xl border border-border/70 bg-card p-2">
                    {openExposures.length === 0 ? (
                      <p className="px-2 py-1 text-sm text-muted-foreground">No open vulnerabilities on this device.</p>
                    ) : (
                      openExposures
                        .sort((a, b) => severityOrder(b.severity) - severityOrder(a.severity))
                        .map((exposure) => {
                          const isSelected = selectedVulnerabilityIds.includes(exposure.vulnerabilityId)
                          return (
                            <label
                              key={exposure.exposureId}
                              className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-sm hover:bg-muted/40"
                            >
                              <Checkbox
                                checked={isSelected}
                                onCheckedChange={(checked) => {
                                  setSelectedVulnerabilityIds((prev) =>
                                    checked
                                      ? [...prev, exposure.vulnerabilityId]
                                      : prev.filter((id) => id !== exposure.vulnerabilityId),
                                  )
                                }}
                              />
                              <span className="font-medium">{exposure.externalId}</span>
                              <Badge variant="outline" className="rounded-full border-border/70 text-xs">
                                {exposure.severity}
                              </Badge>
                              <span className="truncate text-xs text-muted-foreground">{exposure.title}</span>
                            </label>
                          )
                        })
                    )}
                  </div>
                ) : null}
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

function severityOrder(severity: string): number {
  switch (severity.toLowerCase()) {
    case 'critical': return 4
    case 'high': return 3
    case 'medium': return 2
    case 'low': return 1
    default: return 0
  }
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
