import { useState } from 'react'
import { ScanningToolEditor } from './ScanningToolEditor'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { Plus, Trash2 } from 'lucide-react'
import {
  createScanningTool,
  deleteScanningTool,
  fetchScanningTools,
} from '@/api/authenticated-scans.functions'
import type { PagedScanningTools, ScanningTool } from '@/api/authenticated-scans.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

type Props = {
  initialData: PagedScanningTools
  toolDetail?: ScanningTool
  currentScript?: string
  page: number
  pageSize: number
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
  onSelectTool: (id: string) => void
  onDeselectTool: () => void
}

const scriptTypes = ['python', 'bash', 'powershell'] as const
const defaultInterpreters: Record<string, string> = {
  python: '/usr/bin/python3',
  bash: '/bin/bash',
  powershell: '/usr/bin/pwsh',
}

export function ScanningToolsTab({ initialData, toolDetail, currentScript, page, pageSize, onPageChange, onPageSizeChange, onSelectTool, onDeselectTool }: Props) {
  if (toolDetail) {
    return (
      <ScanningToolEditor
        tool={toolDetail}
        initialScript={currentScript ?? ''}
        onBack={onDeselectTool}
      />
    )
  }

  const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [scriptType, setScriptType] = useState<(typeof scriptTypes)[number]>('python')
  const [interpreterPath, setInterpreterPath] = useState(defaultInterpreters.python)
  const [timeoutSeconds, setTimeoutSeconds] = useState(300)
  const [initialScript, setInitialScript] = useState('')

  const query = useQuery({
    queryKey: ['scanning-tools', page, pageSize],
    queryFn: () => fetchScanningTools({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createScanningTool,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      setCreateOpen(false)
      resetForm()
      toast.success('Scanning tool created')
    },
    onError: () => toast.error('Failed to create scanning tool'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteScanningTool,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      toast.success('Scanning tool deleted')
    },
    onError: () => toast.error('Failed to delete scanning tool'),
  })

  const resetForm = () => {
    setName('')
    setDescription('')
    setScriptType('python')
    setInterpreterPath(defaultInterpreters.python)
    setTimeoutSeconds(300)
    setInitialScript('')
  }

  const columns: ColumnDef<ScanningTool>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <button
          type="button"
          className="text-primary underline text-left"
          onClick={() => onSelectTool(row.original.id)}
        >
          {row.original.name}
        </button>
      ),
    },
    {
      accessorKey: 'scriptType',
      header: 'Type',
      cell: ({ row }) => <Badge variant="outline">{row.original.scriptType}</Badge>,
    },
    {
      accessorKey: 'outputModel',
      header: 'Output',
      cell: ({ row }) => <Badge variant="secondary">{row.original.outputModel}</Badge>,
    },
    {
      accessorKey: 'timeoutSeconds',
      header: 'Timeout',
      cell: ({ row }) => `${row.original.timeoutSeconds}s`,
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <Button
          size="sm"
          variant="ghost"
          className="text-destructive"
          onClick={() => {
            if (confirm(`Delete "${row.original.name}"?`)) {
              deleteMutation.mutate({ data: { id: row.original.id } })
            }
          }}
        >
          <Trash2 className="h-3 w-3" />
        </Button>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle>Scanning Tools</CardTitle>
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="mr-1 h-4 w-4" />
            New Tool
          </Button>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            totalPages={query.data?.totalPages ?? 0}
            onPageChange={onPageChange}
            onPageSizeChange={onPageSizeChange ?? (() => {})}
          />
        </CardContent>
      </Card>

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>New Scanning Tool</DialogTitle>
            <DialogDescription>Define a script that runs on target hosts and produces structured output.</DialogDescription>
          </DialogHeader>
          <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-1">
            <div>
              <Label>Name</Label>
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="linux-software-inventory" />
            </div>
            <div>
              <Label>Description</Label>
              <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <Label>Script Type</Label>
                <Select
                  value={scriptType}
                  onValueChange={(v) => {
                    const st = v as (typeof scriptTypes)[number]
                    setScriptType(st)
                    setInterpreterPath(defaultInterpreters[st] ?? '')
                  }}
                >
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    {scriptTypes.map((t) => (
                      <SelectItem key={t} value={t}>{t}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div>
                <Label>Timeout (seconds)</Label>
                <Input type="number" value={timeoutSeconds} onChange={(e) => setTimeoutSeconds(Number(e.target.value))} min={5} max={3600} />
              </div>
            </div>
            <div>
              <Label>Interpreter Path</Label>
              <Input value={interpreterPath} onChange={(e) => setInterpreterPath(e.target.value)} />
            </div>
            <div>
              <Label>Initial Script</Label>
              <Textarea
                value={initialScript}
                onChange={(e) => setInitialScript(e.target.value)}
                rows={8}
                className="font-mono text-xs"
                placeholder="# Your script here..."
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button
              onClick={() =>
                createMutation.mutate({
                  data: { name, description, scriptType, interpreterPath, timeoutSeconds, initialScript },
                })
              }
              disabled={!name || !interpreterPath || !initialScript || createMutation.isPending}
            >
              Create Tool
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
