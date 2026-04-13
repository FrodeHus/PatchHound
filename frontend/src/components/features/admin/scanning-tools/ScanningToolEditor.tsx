import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { ArrowLeft, FileJson, Save } from 'lucide-react'
import { publishToolVersion, updateScanningTool } from '@/api/authenticated-scans.functions'
import type { ScanningTool, ScanningToolVersion } from '@/api/authenticated-scans.schemas'
import { ScriptEditor } from '@/components/ui/script-editor'
import { ExpectedOutputPanel } from './ExpectedOutputPanel'
import { VersionHistoryPanel } from './VersionHistoryPanel'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

const scriptTypes = ['python', 'bash', 'powershell'] as const
const defaultInterpreters: Record<string, string> = {
  python: '/usr/bin/python3',
  bash: '/bin/bash',
  powershell: '/usr/bin/pwsh',
}

type Props = {
  tool: ScanningTool
  initialScript: string
  onBack: () => void
}

export function ScanningToolEditor({ tool, initialScript, onBack }: Props) {
  const queryClient = useQueryClient()

  const [showExpectedOutput, setShowExpectedOutput] = useState(false)

  // Metadata state
  const [name, setName] = useState(tool.name)
  const [description, setDescription] = useState(tool.description)
  const [scriptType, setScriptType] = useState<(typeof scriptTypes)[number]>(
    tool.scriptType as (typeof scriptTypes)[number],
  )
  const [interpreterPath, setInterpreterPath] = useState(tool.interpreterPath)
  const [timeoutSeconds, setTimeoutSeconds] = useState(tool.timeoutSeconds)

  // Script state
  const [scriptContent, setScriptContent] = useState(initialScript)
  const [viewingVersion, setViewingVersion] = useState<ScanningToolVersion | null>(null)

  const metadataMutation = useMutation({
    mutationFn: updateScanningTool,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      toast.success('Tool metadata updated')
    },
    onError: () => toast.error('Failed to update tool metadata'),
  })

  const publishMutation = useMutation({
    mutationFn: publishToolVersion,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['tool-versions', tool.id] })
      void queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      toast.success('New version published')
    },
    onError: () => toast.error('Failed to publish version'),
  })

  const handleViewVersion = (version: ScanningToolVersion) => {
    setViewingVersion(version)
    setScriptContent(version.scriptContent)
  }

  const handleRestoreEditor = () => {
    setViewingVersion(null)
    // Script stays as-is — user can publish it or discard
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Button variant="ghost" size="sm" onClick={onBack}>
          <ArrowLeft className="mr-1 h-4 w-4" />
          Back
        </Button>
        <h2 className="text-lg font-semibold">{tool.name}</h2>
        <Badge variant="outline">{tool.scriptType}</Badge>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setShowExpectedOutput(!showExpectedOutput)}
        >
          <FileJson className="mr-1 h-4 w-4" />
          {showExpectedOutput ? 'Hide' : 'Show'} Expected Output
        </Button>
      </div>

      <div className="grid gap-4 lg:grid-cols-[1fr_300px]">
        {/* Left: metadata + editor */}
        <div className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Tool Settings</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <Label>Name</Label>
                  <Input value={name} onChange={(e) => setName(e.target.value)} />
                </div>
                <div>
                  <Label>Timeout (seconds)</Label>
                  <Input
                    type="number"
                    value={timeoutSeconds}
                    onChange={(e) => setTimeoutSeconds(Number(e.target.value))}
                    min={5}
                    max={3600}
                  />
                </div>
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
                      if (!v) return
                      const st = v
                      setScriptType(st)
                      setInterpreterPath(defaultInterpreters[st] ?? interpreterPath)
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
                  <Label>Interpreter Path</Label>
                  <Input value={interpreterPath} onChange={(e) => setInterpreterPath(e.target.value)} />
                </div>
              </div>
              <Button
                size="sm"
                onClick={() =>
                  metadataMutation.mutate({
                    data: { id: tool.id, name, description, scriptType, interpreterPath, timeoutSeconds },
                  })
                }
                disabled={!name || !interpreterPath || metadataMutation.isPending}
              >
                Save Settings
              </Button>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-base">Script</CardTitle>
                {viewingVersion && (
                  <p className="text-muted-foreground text-xs mt-1">
                    Viewing v{viewingVersion.versionNumber} —{' '}
                    <button
                      type="button"
                      className="text-primary underline"
                      onClick={handleRestoreEditor}
                    >
                      back to editor
                    </button>
                  </p>
                )}
              </div>
              <Button
                size="sm"
                onClick={() =>
                  publishMutation.mutate({ data: { toolId: tool.id, scriptContent } })
                }
                disabled={!scriptContent || publishMutation.isPending}
              >
                <Save className="mr-1 h-3 w-3" />
                Publish Version
              </Button>
            </CardHeader>
            <CardContent>
              <ScriptEditor
                value={scriptContent}
                onChange={viewingVersion ? undefined : setScriptContent}
                language={scriptType}
                readOnly={!!viewingVersion}
                height="400px"
              />
            </CardContent>
          </Card>
        </div>

        {/* Right: version history */}
        <div>
          <VersionHistoryPanel
            toolId={tool.id}
            currentVersionId={tool.currentVersionId}
            onViewVersion={handleViewVersion}
          />
        </div>
      </div>

      {showExpectedOutput && <ExpectedOutputPanel />}
    </div>
  )
}
