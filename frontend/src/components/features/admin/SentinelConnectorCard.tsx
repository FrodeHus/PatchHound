import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Satellite, Loader2, ExternalLinkIcon } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Badge } from '@/components/ui/badge'
import {
  fetchSentinelConnector,
  updateSentinelConnector,
} from '@/api/integrations.functions'
import type { UpdateSentinelConnectorInput } from '@/api/integrations.schemas'

const CONNECTOR_STUDIO_URL =
  'https://connector-studio.reothor.no/?project=https://raw.githubusercontent.com/FrodeHus/PatchHound/main/PatchHound-project.json'

const CONNECTOR_STUDIO_BADGE_URL = 'https://connector-studio.reothor.no/badge.svg'

export function SentinelConnectorCard() {
  const queryClient = useQueryClient()
  const { data: config, isLoading } = useQuery({
    queryKey: ['sentinel-connector'],
    queryFn: () => fetchSentinelConnector(),
  })

  const [editing, setEditing] = useState(false)

  if (isLoading || !config) {
    return (
      <Card className="rounded-2xl border-border/70">
        <CardContent className="flex items-center justify-center py-12">
          <Loader2 className="size-5 animate-spin text-muted-foreground" />
        </CardContent>
      </Card>
    )
  }

  const isConfigured = !!(config.dceEndpoint && config.dcrImmutableId && config.streamName)

  if (!editing && !isConfigured) {
    return (
      <Card className="rounded-2xl border-border/70">
        <CardHeader>
          <div className="flex items-center gap-3">
            <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
              <Satellite className="size-5 text-primary" />
            </div>
            <div>
              <CardTitle>Microsoft Sentinel Data Connector</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Forward audit trail events to a Microsoft Sentinel workspace via the Logs Ingestion API.
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <p className="text-sm text-muted-foreground">
            Not configured. You will need a Data Collection Endpoint, Data Collection Rule, and an Entra app registration with the Monitoring Metrics Publisher role.
          </p>
          <SentinelConnectorStudioCallout />
          <Button onClick={() => setEditing(true)}>Configure connector</Button>
        </CardContent>
      </Card>
    )
  }

  if (!editing) {
    return (
      <Card className="rounded-2xl border-border/70">
        <CardHeader>
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
                <Satellite className="size-5 text-primary" />
              </div>
              <div>
                <CardTitle>Microsoft Sentinel Data Connector</CardTitle>
                <p className="mt-1 text-sm text-muted-foreground">
                  Audit event forwarding via Logs Ingestion API
                </p>
              </div>
            </div>
            <Badge variant={config.enabled ? 'default' : 'secondary'}>
              {config.enabled ? 'Enabled' : 'Disabled'}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="grid gap-2 text-sm sm:grid-cols-2">
            <div>
              <span className="text-muted-foreground">DCE Endpoint</span>
              <p className="truncate font-mono text-xs">{config.dceEndpoint}</p>
            </div>
            <div>
              <span className="text-muted-foreground">DCR Immutable ID</span>
              <p className="truncate font-mono text-xs">{config.dcrImmutableId}</p>
            </div>
            <div>
              <span className="text-muted-foreground">Stream Name</span>
              <p className="font-mono text-xs">{config.streamName}</p>
            </div>
            <div>
              <span className="text-muted-foreground">Client Secret</span>
              <p className="text-xs">{config.hasSecret ? '••••••••' : 'Not set'}</p>
            </div>
          </div>
          <Button variant="outline" onClick={() => setEditing(true)}>
            Edit configuration
          </Button>
        </CardContent>
      </Card>
    )
  }

  return (
    <SentinelConnectorForm
      config={config}
      onClose={() => setEditing(false)}
      onSaved={() => {
        void queryClient.invalidateQueries({ queryKey: ['sentinel-connector'] })
        setEditing(false)
      }}
    />
  )
}

function SentinelConnectorForm({
  config,
  onClose,
  onSaved,
}: {
  config: { enabled: boolean; dceEndpoint: string; dcrImmutableId: string; streamName: string; tenantId: string; clientId: string; hasSecret: boolean }
  onClose: () => void
  onSaved: () => void
}) {
  const [form, setForm] = useState<UpdateSentinelConnectorInput>({
    enabled: config.enabled,
    dceEndpoint: config.dceEndpoint,
    dcrImmutableId: config.dcrImmutableId,
    streamName: config.streamName || 'Custom-PatchHoundAuditLog',
    tenantId: config.tenantId,
    clientId: config.clientId,
    clientSecret: null,
  })

  const mutation = useMutation({
    mutationFn: (data: UpdateSentinelConnectorInput) => updateSentinelConnector({ data }),
    onSuccess: () => onSaved(),
  })

  const update = (field: keyof UpdateSentinelConnectorInput, value: string | boolean | null) =>
    setForm((prev) => ({ ...prev, [field]: value }))

  return (
    <Card className="rounded-2xl border-border/70">
      <CardHeader>
        <div className="flex items-center gap-3">
          <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
            <Satellite className="size-5 text-primary" />
          </div>
          <div>
            <CardTitle>Microsoft Sentinel Data Connector</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              Deploy the PatchHound data connector first, then copy the generated values into this form.
            </p>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <SentinelConnectorStudioCallout />

        <div className="flex items-center gap-3">
          <Switch
            id="sentinel-enabled"
            checked={form.enabled}
            onCheckedChange={(checked) => update('enabled', checked)}
          />
          <Label htmlFor="sentinel-enabled">Enable connector</Label>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="dce-endpoint">DCE Endpoint</Label>
            <Input
              id="dce-endpoint"
              placeholder="https://<name>.ingest.monitor.azure.com"
              value={form.dceEndpoint}
              onChange={(e) => update('dceEndpoint', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="dcr-id">DCR Immutable ID</Label>
            <Input
              id="dcr-id"
              placeholder="dcr-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={form.dcrImmutableId}
              onChange={(e) => update('dcrImmutableId', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="stream-name">Stream Name</Label>
            <Input
              id="stream-name"
              placeholder="Custom-PatchHoundAuditLog"
              value={form.streamName}
              onChange={(e) => update('streamName', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="entra-tenant">Entra Tenant ID</Label>
            <Input
              id="entra-tenant"
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={form.tenantId}
              onChange={(e) => update('tenantId', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="client-id">Client ID</Label>
            <Input
              id="client-id"
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={form.clientId}
              onChange={(e) => update('clientId', e.target.value)}
            />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="client-secret">
              Client Secret
              {config.hasSecret && (
                <span className="ml-2 text-xs text-muted-foreground">(leave blank to keep current)</span>
              )}
            </Label>
            <Input
              id="client-secret"
              type="password"
              placeholder={config.hasSecret ? '••••••••' : 'Enter client secret'}
              value={form.clientSecret ?? ''}
              onChange={(e) => update('clientSecret', e.target.value || null)}
            />
          </div>
        </div>

        <div className="flex gap-2">
          <Button
            onClick={() => mutation.mutate(form)}
            disabled={mutation.isPending}
          >
            {mutation.isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
            Save
          </Button>
          <Button variant="outline" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
        </div>

        {mutation.isError && (
          <p className="text-sm text-destructive">
            {mutation.error instanceof Error ? mutation.error.message : 'Failed to save configuration'}
          </p>
        )}
      </CardContent>
    </Card>
  )
}

function SentinelConnectorStudioCallout() {
  return (
    <div className="rounded-2xl border border-border/70 bg-muted/30 p-4">
      <p className="text-sm text-foreground">
        To set up the Sentinel integration, first deploy the PatchHound data connector. Opening the link below guides you through that deployment in Connector Studio.
      </p>
      <a
        href={CONNECTOR_STUDIO_URL}
        target="_blank"
        rel="noreferrer"
        className="mt-3 inline-flex items-center gap-2 rounded-xl border border-border/70 bg-background px-3 py-2 transition-colors hover:bg-muted"
      >
        <img
          src={CONNECTOR_STUDIO_BADGE_URL}
          alt="Open in Connector Studio"
          className="h-5 w-auto"
        />
        <span className="text-sm font-medium">Open in Connector Studio</span>
        <ExternalLinkIcon className="size-3.5 text-muted-foreground" />
      </a>
    </div>
  )
}
