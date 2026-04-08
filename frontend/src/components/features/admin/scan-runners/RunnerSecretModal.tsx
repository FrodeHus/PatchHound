import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  runnerName: string
  runnerId: string
  bearerSecret: string
  centralUrl?: string
}

export function RunnerSecretModal({ open, onOpenChange, runnerName, runnerId, bearerSecret, centralUrl }: Props) {
  const [copied, setCopied] = useState<string | null>(null)

  const yamlSnippet = `# runner.yaml for ${runnerName}
centralUrl: "${centralUrl ?? 'https://your-patchhound-instance.com'}"
bearerToken: "${bearerSecret}"
maxConcurrentJobs: 10
pollIntervalSeconds: 10
heartbeatIntervalSeconds: 30`

  const copyToClipboard = async (text: string, label: string) => {
    await navigator.clipboard.writeText(text)
    setCopied(label)
    setTimeout(() => setCopied(null), 2000)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>Runner Created: {runnerName}</DialogTitle>
          <DialogDescription>
            Save the bearer secret below — it will not be shown again.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div>
            <label className="text-sm font-medium">Runner ID</label>
            <div className="mt-1 flex items-center gap-2">
              <code className="bg-muted rounded px-2 py-1 text-sm flex-1 break-all">{runnerId}</code>
              <Button size="icon" variant="ghost" onClick={() => copyToClipboard(runnerId, 'id')}>
                {copied === 'id' ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              </Button>
            </div>
          </div>

          <div>
            <label className="text-sm font-medium">Bearer Secret</label>
            <div className="mt-1 flex items-center gap-2">
              <code className="bg-muted rounded px-2 py-1 text-sm flex-1 break-all font-mono">{bearerSecret}</code>
              <Button size="icon" variant="ghost" onClick={() => copyToClipboard(bearerSecret, 'secret')}>
                {copied === 'secret' ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              </Button>
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between">
              <label className="text-sm font-medium">runner.yaml</label>
              <Button size="sm" variant="ghost" onClick={() => copyToClipboard(yamlSnippet, 'yaml')}>
                {copied === 'yaml' ? <Check className="mr-1 h-3 w-3" /> : <Copy className="mr-1 h-3 w-3" />}
                {copied === 'yaml' ? 'Copied' : 'Copy'}
              </Button>
            </div>
            <pre className="bg-muted mt-1 rounded p-3 text-xs overflow-x-auto whitespace-pre">{yamlSnippet}</pre>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
