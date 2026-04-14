import { createFileRoute, redirect } from '@tanstack/react-router'
import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Flag, Plus, Trash2 } from 'lucide-react'
import {
  deleteFeatureFlagOverride,
  fetchAdminFeatureFlags,
  fetchFeatureFlagOverrides,
  upsertFeatureFlagOverride,
} from '@/api/feature-flags.functions'
import type { AdminFeatureFlag, FeatureFlagOverride } from '@/api/feature-flags.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { getApiErrorMessage } from '@/lib/api-errors'

export const Route = createFileRoute('/_authed/admin/platform/feature-flags')({
  beforeLoad: ({ context }) => {
    if (!(context.user?.activeRoles ?? []).includes('GlobalAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  component: FeatureFlagsPage,
})

function stageBadgeVariant(stage: string): 'default' | 'secondary' | 'outline' | 'destructive' {
  switch (stage) {
    case 'Experimental': return 'outline'
    case 'Preview': return 'secondary'
    case 'GenerallyAvailable': return 'default'
    case 'Deprecated': return 'destructive'
    default: return 'outline'
  }
}

function stageLabel(stage: string): string {
  switch (stage) {
    case 'GenerallyAvailable': return 'GA'
    default: return stage
  }
}

type NewOverrideForm = {
  flagName: string
  targetType: 'tenant' | 'user'
  targetId: string
  isEnabled: boolean
  expiresAt: string
}

function FeatureFlagsPage() {
  const flagsQuery = useQuery({
    queryKey: ['admin-feature-flags'],
    queryFn: () => fetchAdminFeatureFlags(),
    staleTime: 30_000,
  })

  const overridesQuery = useQuery({
    queryKey: ['feature-flag-overrides'],
    queryFn: () => fetchFeatureFlagOverrides({ data: {} }),
    staleTime: 30_000,
  })

  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState<NewOverrideForm>({
    flagName: '',
    targetType: 'tenant',
    targetId: '',
    isEnabled: true,
    expiresAt: '',
  })

  const upsertMutation = useMutation({
    mutationFn: async () => {
      await upsertFeatureFlagOverride({
        data: {
          flagName: form.flagName,
          tenantId: form.targetType === 'tenant' ? form.targetId : undefined,
          userId: form.targetType === 'user' ? form.targetId : undefined,
          isEnabled: form.isEnabled,
          expiresAt: form.expiresAt ? form.expiresAt : undefined,
        },
      })
    },
    onSuccess: async () => {
      toast.success('Override saved')
      setShowForm(false)
      setForm({ flagName: '', targetType: 'tenant', targetId: '', isEnabled: true, expiresAt: '' })
      await Promise.all([flagsQuery.refetch(), overridesQuery.refetch()])
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to save override'))
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteFeatureFlagOverride({ data: { id } }),
    onSuccess: async () => {
      toast.success('Override removed')
      await Promise.all([flagsQuery.refetch(), overridesQuery.refetch()])
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to remove override'))
    },
  })

  const flags: AdminFeatureFlag[] = flagsQuery.data ?? []
  const overrides: FeatureFlagOverride[] = overridesQuery.data ?? []

  return (
    <section className="space-y-5 pb-4">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Platform configuration</p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">Feature Flags</h1>
            <p className="max-w-2xl text-sm text-muted-foreground">
              View the global state of all feature flags and manage per-tenant or per-user overrides.
              Overrides take precedence over the global configuration default.
            </p>
          </div>
        </div>
      </div>

      <Card className="rounded-2xl border-border/70 bg-card/85">
        <CardHeader>
          <CardTitle>Registered flags</CardTitle>
          <CardDescription>
            Global resolved state for each flag — before any per-tenant or per-user overrides.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {flags.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {flagsQuery.isLoading ? 'Loading…' : 'No flags registered.'}
            </p>
          ) : (
            <div className="divide-y divide-border/50">
              {flags.map((flag) => (
                <div key={flag.flagName} className="flex items-center justify-between gap-3 py-3">
                  <div className="space-y-0.5">
                    <div className="flex items-center gap-2">
                      <Flag className="size-4 text-primary" />
                      <span className="font-medium">{flag.displayName}</span>
                      <Badge variant={stageBadgeVariant(flag.stage)} className="rounded-full text-xs">
                        {stageLabel(flag.stage)}
                      </Badge>
                    </div>
                    <p className="pl-6 text-xs text-muted-foreground">{flag.flagName}</p>
                  </div>
                  <Badge
                    variant={flag.isEnabled ? 'default' : 'outline'}
                    className="rounded-full"
                  >
                    {flag.isEnabled ? 'Enabled' : 'Disabled'}
                  </Badge>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card className="rounded-2xl border-border/70 bg-card/85">
        <CardHeader>
          <div className="flex items-center justify-between gap-3">
            <div>
              <CardTitle>Overrides</CardTitle>
              <CardDescription>
                Per-tenant and per-user overrides. Highest-precedence override wins at evaluation time.
              </CardDescription>
            </div>
            <Button
              variant="outline"
              size="sm"
              className="gap-2"
              onClick={() => setShowForm((v) => !v)}
            >
              <Plus className="size-4" />
              Add override
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {showForm && (
            <div className="rounded-2xl border border-border/70 bg-background/30 p-4 space-y-4">
              <p className="text-sm font-medium">New override</p>
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="space-y-1.5">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Flag</span>
                  <select
                    className="flex h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                    value={form.flagName}
                    onChange={(e) => setForm((f) => ({ ...f, flagName: e.target.value }))}
                  >
                    <option value="">Select a flag…</option>
                    {flags.map((flag) => (
                      <option key={flag.flagName} value={flag.flagName}>
                        {flag.displayName}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="space-y-1.5">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Target type</span>
                  <select
                    className="flex h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                    value={form.targetType}
                    onChange={(e) =>
                      setForm((f) => ({ ...f, targetType: e.target.value as 'tenant' | 'user' }))
                    }
                  >
                    <option value="tenant">Tenant</option>
                    <option value="user">User</option>
                  </select>
                </label>
                <label className="space-y-1.5">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    {form.targetType === 'tenant' ? 'Tenant ID' : 'User ID'}
                  </span>
                  <input
                    className="flex h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                    type="text"
                    placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                    value={form.targetId}
                    onChange={(e) => setForm((f) => ({ ...f, targetId: e.target.value }))}
                  />
                </label>
                <label className="space-y-1.5">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Expires at (optional)</span>
                  <input
                    className="flex h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                    type="datetime-local"
                    value={form.expiresAt}
                    onChange={(e) => setForm((f) => ({ ...f, expiresAt: e.target.value }))}
                  />
                </label>
              </div>
              <label className="flex items-center gap-3 rounded-2xl border border-border/70 bg-background/20 px-4 py-3">
                <input
                  type="checkbox"
                  checked={form.isEnabled}
                  onChange={(e) => setForm((f) => ({ ...f, isEnabled: e.target.checked }))}
                />
                <div>
                  <p className="text-sm font-medium">Enabled for this target</p>
                  <p className="text-xs text-muted-foreground">
                    Uncheck to explicitly disable the flag for this tenant or user.
                  </p>
                </div>
              </label>
              <div className="flex gap-3">
                <Button
                  size="sm"
                  onClick={() => upsertMutation.mutate()}
                  disabled={upsertMutation.isPending || !form.flagName || !form.targetId}
                >
                  {upsertMutation.isPending ? 'Saving…' : 'Save override'}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    setShowForm(false)
                    setForm({ flagName: '', targetType: 'tenant', targetId: '', isEnabled: true, expiresAt: '' })
                  }}
                >
                  Cancel
                </Button>
              </div>
            </div>
          )}

          {overrides.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {overridesQuery.isLoading ? 'Loading…' : 'No overrides configured.'}
            </p>
          ) : (
            <div className="divide-y divide-border/50">
              {overrides.map((override) => (
                <div
                  key={override.id}
                  className="flex items-center justify-between gap-3 py-3"
                >
                  <div className="space-y-0.5 text-sm">
                    <p className="font-medium">{override.flagName}</p>
                    <p className="text-xs text-muted-foreground">
                      {override.tenantId ? `Tenant: ${override.tenantId}` : `User: ${override.userId}`}
                      {override.expiresAt && ` · Expires ${new Date(override.expiresAt).toLocaleDateString()}`}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge variant={override.isEnabled ? 'default' : 'outline'} className="rounded-full">
                      {override.isEnabled ? 'Enabled' : 'Disabled'}
                    </Badge>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="size-8 text-muted-foreground hover:text-destructive"
                      onClick={() => deleteMutation.mutate(override.id)}
                      disabled={deleteMutation.isPending}
                    >
                      <Trash2 className="size-4" />
                      <span className="sr-only">Remove override</span>
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </section>
  )
}
