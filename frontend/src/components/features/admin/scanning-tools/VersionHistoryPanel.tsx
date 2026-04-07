import { useQuery } from '@tanstack/react-query'
import { Eye } from 'lucide-react'
import { fetchToolVersions } from '@/api/authenticated-scans.functions'
import type { ScanningToolVersion } from '@/api/authenticated-scans.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatDateTime } from '@/lib/formatting'

type Props = {
  toolId: string
  currentVersionId: string | null
  onViewVersion: (version: ScanningToolVersion) => void
}

export function VersionHistoryPanel({ toolId, currentVersionId, onViewVersion }: Props) {
  const query = useQuery({
    queryKey: ['tool-versions', toolId],
    queryFn: () => fetchToolVersions({ data: { toolId } }),
  })

  const versions = query.data ?? []

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Version History</CardTitle>
      </CardHeader>
      <CardContent>
        {versions.length === 0 ? (
          <p className="text-muted-foreground text-sm">No versions yet.</p>
        ) : (
          <div className="space-y-2">
            {versions.map((v) => (
              <div
                key={v.id}
                className="flex items-center justify-between rounded-lg border px-3 py-2 text-sm"
              >
                <div className="space-y-0.5">
                  <div className="flex items-center gap-2">
                    <span className="font-medium">v{v.versionNumber}</span>
                    {v.id === currentVersionId && (
                      <Badge variant="default" className="text-xs">Current</Badge>
                    )}
                  </div>
                  <p className="text-muted-foreground text-xs">
                    {formatDateTime(v.editedAt)}
                  </p>
                </div>
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => onViewVersion(v)}
                >
                  <Eye className="mr-1 h-3 w-3" />
                  View
                </Button>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
