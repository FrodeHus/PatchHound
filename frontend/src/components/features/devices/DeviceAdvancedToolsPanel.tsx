import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { Braces, Expand, Play } from 'lucide-react'
import { fetchAdvancedTools, runAdvancedToolForAsset } from '@/api/advanced-tools.functions'
import type { AdvancedToolAssetExecutionResult, AdvancedToolCatalog } from '@/api/advanced-tools.schemas'
import type { DeviceDetail } from '@/api/devices.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

// Phase 1 canonical cleanup (Task 15): device-native advanced tools
// panel. The previous vulnerability picker UI relied on
// AssetDetail.vulnerabilities which no longer exists on the canonical
// DeviceDetail contract — Phase 5 will reintroduce per-vulnerability
// scoping once vulnerability tables are rewired onto Device. For now,
// advanced tool runs always target all open vulnerabilities on the
// device.

type Props = {
  device: DeviceDetail
}

export function DeviceAdvancedToolsPanel({ device }: Props) {
  const [selectedToolId, setSelectedToolId] = useState<string>('')
  const [rawResultsOpen, setRawResultsOpen] = useState(false)
  const [reportOpen, setReportOpen] = useState(false)

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
          useAllOpenVulnerabilities: true,
          vulnerabilityIds: [],
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
                disabled={!selectedToolId || runMutation.isPending}
                onClick={() => runMutation.mutate()}
              >
                <Play className="mr-2 size-4" />
                Run tool
              </Button>
            </div>
          </div>
        </div>

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

function formatCellValue(value: unknown) {
  if (value === null || value === undefined) {
    return ''
  }

  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }

  return JSON.stringify(value)
}
