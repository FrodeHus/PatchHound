import { createFileRoute, Link } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { toast } from 'sonner'
import { assignCloudApplicationOwner, fetchCloudApplicationDetail } from '@/api/cloud-applications.functions'
import { fetchTeams } from '@/api/teams.functions'
import type { CloudApplicationCredential } from '@/api/cloud-applications.schemas'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ChevronLeft, Key, Shield, ExternalLink } from 'lucide-react'

export const Route = createFileRoute('/_authed/assets/applications/$id')({
  loader: ({ params }) => fetchCloudApplicationDetail({ data: { id: params.id } }),
  component: ApplicationDetailPage,
})

function formatExpiryDate(expiresAt: string): string {
  return new Date(expiresAt).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function formatExpiryStatus(expiresAt: string): { label: string; variant: 'destructive' | 'outline' | 'secondary' } {
  const now = new Date()
  const expiry = new Date(expiresAt)
  const diffMs = expiry.getTime() - now.getTime()
  const days = Math.floor(diffMs / (1000 * 60 * 60 * 24))

  if (diffMs < 0) return { label: 'Expired', variant: 'destructive' }
  if (days <= 30) return { label: `Expires in ${days === 0 ? 'today' : days === 1 ? '1 day' : `${days} days`}`, variant: 'outline' }
  return { label: formatExpiryDate(expiresAt), variant: 'secondary' }
}

function CredentialRow({ cred }: { cred: CloudApplicationCredential }) {
  const status = formatExpiryStatus(cred.expiresAt)
  return (
    <div className="flex items-center justify-between py-3 border-b border-border/60 last:border-0">
      <div className="flex items-center gap-3 min-w-0">
        <span className="flex size-8 shrink-0 items-center justify-center rounded-lg border border-border/60 bg-background/40">
          <Key className="size-3.5 text-muted-foreground" />
        </span>
        <div className="min-w-0">
          <div className="text-sm font-medium truncate">{cred.displayName ?? '(unnamed)'}</div>
          <div className="text-xs text-muted-foreground">{cred.type}</div>
        </div>
      </div>
      <Badge
        variant={status.variant}
        className={`text-xs shrink-0 ml-4 ${status.variant === 'outline' ? 'border-yellow-500 text-yellow-600' : ''}`}
      >
        {status.label}
      </Badge>
    </div>
  )
}

function MetaRow({ label, value, mono = false }: { label: string; value: React.ReactNode; mono?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5 py-3 border-b border-border/60 last:border-0 sm:flex-row sm:items-center sm:justify-between">
      <span className="text-sm text-muted-foreground shrink-0 sm:w-48">{label}</span>
      <span className={`text-sm font-medium break-all ${mono ? 'font-mono text-xs' : ''}`}>{value}</span>
    </div>
  )
}

function ApplicationDetailPage() {
  const initialData = Route.useLoaderData()
  const { id } = Route.useParams()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const queryClient = useQueryClient()

  const appQuery = useQuery({
    queryKey: ['cloud-application', selectedTenantId, id],
    queryFn: () => fetchCloudApplicationDetail({ data: { id } }),
    initialData: canUseInitialData ? initialData : undefined,
  })
  const app = appQuery.data ?? (canUseInitialData ? initialData : undefined)

  const teamsQuery = useQuery({
    queryKey: ['teams', selectedTenantId],
    queryFn: () => fetchTeams({ data: { tenantId: selectedTenantId ?? undefined } }),
    enabled: Boolean(selectedTenantId),
  })

  const ownerMutation = useMutation({
    mutationFn: (teamId: string | null) =>
      assignCloudApplicationOwner({ data: { id, teamId } }),
    onSuccess: () => {
      toast.success('Owner group updated.')
      void queryClient.invalidateQueries({ queryKey: ['cloud-application', selectedTenantId, id] })
    },
    onError: () => toast.error('Failed to update owner group.'),
  })

  if (!app) return null

  const teams = teamsQuery.data?.items ?? []
  const expiredCount = app.credentials.filter(c => new Date(c.expiresAt) < new Date()).length
  const expiringCount = app.credentials.filter(c => {
    const d = new Date(c.expiresAt)
    const now = new Date()
    return d >= now && d <= new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000)
  }).length

  return (
    <div className="flex flex-col gap-6 p-6 max-w-3xl">
      <div className="flex items-center gap-3">
        <Link
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          {...({ to: '/assets/applications', search: {} } as any)}
          className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          <ChevronLeft className="size-4" />
          Applications
        </Link>
      </div>

      <div className="flex items-start gap-3">
        <span className="flex size-11 shrink-0 items-center justify-center rounded-2xl border border-border/60 bg-background/40">
          <Shield className="size-5 text-muted-foreground" />
        </span>
        <div>
          <h1 className="text-xl font-semibold">{app.name}</h1>
          {app.description && (
            <p className="text-sm text-muted-foreground mt-0.5">{app.description}</p>
          )}
          <div className="flex items-center gap-2 mt-1.5">
            {expiredCount > 0 && (
              <Badge variant="destructive" className="text-xs">{expiredCount} expired</Badge>
            )}
            {expiringCount > 0 && (
              <Badge variant="outline" className="text-xs border-yellow-500 text-yellow-600">
                {expiringCount} expiring soon
              </Badge>
            )}
            {app.isFallbackPublicClient && (
              <Badge variant="secondary" className="text-xs">Device Code Enabled</Badge>
            )}
          </div>
        </div>
      </div>

      {/* Identity */}
      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground mb-2">Identity</h2>
        <div className="rounded-2xl border border-border/60 bg-card/50 px-4">
          <MetaRow label="Display name" value={app.name} />
          <MetaRow label="Client ID (App ID)" value={app.appId ?? '—'} mono />
          <MetaRow label="Object ID" value={app.externalId} mono />
          <MetaRow
            label="Device Code Flow"
            value={
              app.isFallbackPublicClient
                ? <span className="text-amber-600 font-medium">Enabled</span>
                : <span className="text-muted-foreground">Disabled</span>
            }
          />
        </div>
      </section>

      {/* Redirect URIs */}
      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground mb-2">
          Redirect URIs
          <span className="ml-2 text-xs font-normal normal-case text-muted-foreground">
            ({app.redirectUris.length})
          </span>
        </h2>
        <div className="rounded-2xl border border-border/60 bg-card/50 px-4">
          {app.redirectUris.length === 0 ? (
            <p className="text-sm text-muted-foreground py-3">No redirect URIs configured.</p>
          ) : (
            app.redirectUris.map((uri) => (
              <div key={uri} className="flex items-center justify-between py-3 border-b border-border/60 last:border-0">
                <span className="text-xs font-mono break-all text-foreground">{uri}</span>
                <a
                  href={uri}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="ml-3 shrink-0 text-muted-foreground hover:text-foreground"
                  onClick={(e) => e.stopPropagation()}
                >
                  <ExternalLink className="size-3.5" />
                </a>
              </div>
            ))
          )}
        </div>
      </section>

      {/* Credentials */}
      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground mb-2">
          Credentials
          <span className="ml-2 text-xs font-normal normal-case text-muted-foreground">
            ({app.credentials.length})
          </span>
        </h2>
        <div className="rounded-2xl border border-border/60 bg-card/50 px-4">
          {app.credentials.length === 0 ? (
            <p className="text-sm text-muted-foreground py-3">No credentials configured.</p>
          ) : (
            app.credentials.map((cred) => <CredentialRow key={cred.id} cred={cred} />)
          )}
        </div>
      </section>

      {/* Assignment */}
      <section>
        <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground mb-2">Assignment</h2>
        <div className="rounded-2xl border border-border/60 bg-card/50 px-4 py-4">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="text-sm font-medium">Owner group</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                The team responsible for this application.
              </p>
            </div>
            <div className="flex items-center gap-2 shrink-0">
              <Select
                value={app.ownerTeamId ?? 'none'}
                onValueChange={(value) =>
                  ownerMutation.mutate(value === 'none' ? null : value)
                }
                disabled={ownerMutation.isPending}
              >
                <SelectTrigger className="w-48 h-8 text-sm">
                  <SelectValue placeholder="Unassigned">
                    {app.ownerTeamId
                      ? (teams.find(t => t.id === app.ownerTeamId)?.name ?? app.ownerTeamName ?? '…')
                      : <span className="text-muted-foreground">Unassigned</span>
                    }
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">
                    <span className="text-muted-foreground">Unassigned</span>
                  </SelectItem>
                  {teams.map((team) => (
                    <SelectItem key={team.id} value={team.id}>
                      {team.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {app.ownerTeamId && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-8 text-xs text-muted-foreground"
                  onClick={() => ownerMutation.mutate(null)}
                  disabled={ownerMutation.isPending}
                >
                  Clear
                </Button>
              )}
            </div>
          </div>
        </div>
      </section>
    </div>
  )
}
