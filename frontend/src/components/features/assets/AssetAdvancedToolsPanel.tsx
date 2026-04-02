import { useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { Braces, Play } from 'lucide-react'
import { fetchAdvancedTools, runAdvancedToolForAsset } from '@/api/advanced-tools.functions'
import type { AdvancedToolAssetExecutionResult, AdvancedToolCatalog } from '@/api/advanced-tools.schemas'
import type { AssetDetail } from '@/api/assets.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { DataTable } from '@/components/ui/data-table'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

type Props = {
  asset: AssetDetail
}

export function AssetAdvancedToolsPanel({ asset }: Props) {
  const [selectedToolId, setSelectedToolId] = useState<string>('')
  const [useAllOpenVulnerabilities, setUseAllOpenVulnerabilities] = useState(true)
  const [selectedVulnerabilityIds, setSelectedVulnerabilityIds] = useState<string[]>([])

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
  const openVulnerabilities = useMemo(
    () => asset.vulnerabilities.filter((vulnerability) => vulnerability.status !== 'Resolved'),
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
              Investigate where vulnerable components show up on this device and why Defender thinks they are installed.
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
                <SelectValue placeholder="Select an advanced tool" />
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
                onCheckedChange={(checked) => setUseAllOpenVulnerabilities(Boolean(checked))}
              />
              <span>Use all open vulnerabilities</span>
            </label>
            {!useAllOpenVulnerabilities ? (
              <div className="grid gap-2 md:grid-cols-2">
                {openVulnerabilities.map((vulnerability) => (
                  <label
                    key={vulnerability.vulnerabilityId}
                    className="flex items-start gap-3 rounded-2xl border border-border/70 bg-card/80 p-3 text-sm"
                  >
                    <Checkbox
                      checked={selectedVulnerabilityIds.includes(vulnerability.vulnerabilityId)}
                      onCheckedChange={(checked) => {
                        setSelectedVulnerabilityIds((current) =>
                          checked
                            ? [...current, vulnerability.vulnerabilityId]
                            : current.filter((id) => id !== vulnerability.vulnerabilityId),
                        )
                      }}
                    />
                    <div className="space-y-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-medium">{vulnerability.externalId}</span>
                        <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
                          {vulnerability.effectiveSeverity}
                        </Badge>
                      </div>
                      <p className="text-muted-foreground">{vulnerability.title}</p>
                    </div>
                  </label>
                ))}
              </div>
            ) : null}
          </div>
        ) : null}

        {runMutation.data ? <AdvancedToolResultGroups result={runMutation.data} /> : null}
      </CardContent>
    </Card>
  )
}

function AdvancedToolResultGroups({ result }: { result: AdvancedToolAssetExecutionResult }) {
  return (
    <div className="space-y-4">
      {result.queries.map((queryResult, index) => {
        const columns: ColumnDef<Record<string, unknown>>[] = queryResult.schema.map((column) => ({
          accessorKey: column.name,
          header: column.name,
          cell: ({ row }) => <span className="text-xs">{formatCellValue(row.original[column.name])}</span>,
        }))

        return (
          <details
            key={`${queryResult.label}-${index}`}
            className="rounded-2xl border border-border/70 bg-background/60"
            open={index === 0}
          >
            <summary className="cursor-pointer list-none px-4 py-3">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="font-medium">{queryResult.label}</span>
                    {queryResult.vulnerabilityExternalId ? (
                      <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
                        {queryResult.vulnerabilityExternalId}
                      </Badge>
                    ) : null}
                  </div>
                  <p className="text-sm text-muted-foreground">{queryResult.results.length} rows returned</p>
                </div>
              </div>
            </summary>
            <div className="space-y-4 border-t border-border/70 px-4 py-4">
              <div className="rounded-2xl border border-border/70 bg-card/80 p-3">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  Rendered query
                </p>
                <pre className="mt-2 overflow-x-auto text-xs text-muted-foreground">
                  <code>{queryResult.query}</code>
                </pre>
              </div>
              <DataTable
                columns={columns}
                data={queryResult.results as Record<string, unknown>[]}
                emptyState={<span className="text-sm text-muted-foreground">The query returned no rows.</span>}
              />
            </div>
          </details>
        )
      })}
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
