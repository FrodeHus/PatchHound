import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Mail, Send, ShieldCheck } from 'lucide-react'
import {
  fetchNotificationProviders,
  sendMailgunTestEmail,
  updateNotificationProviders,
  validateMailgunConfiguration,
} from '@/server/system.functions'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { getApiErrorMessage } from '@/lib/api-errors'

type DraftState = {
  activeProvider: 'smtp' | 'mailgun'
  mailgun: {
    enabled: boolean
    region: 'us' | 'eu'
    domain: string
    fromAddress: string
    fromName: string
    replyToAddress: string
    apiKey: string
    hasApiKey: boolean
  }
}

function toDraftState(data: Awaited<ReturnType<typeof fetchNotificationProviders>>): DraftState {
  return {
    activeProvider: data.activeProvider,
    mailgun: {
      enabled: data.mailgun.enabled,
      region: data.mailgun.region,
      domain: data.mailgun.domain,
      fromAddress: data.mailgun.fromAddress,
      fromName: data.mailgun.fromName ?? '',
      replyToAddress: data.mailgun.replyToAddress ?? '',
      apiKey: '',
      hasApiKey: data.mailgun.hasApiKey,
    },
  }
}

export function NotificationDeliverySettingsPage() {
  const query = useQuery({
    queryKey: ['notification-providers'],
    queryFn: () => fetchNotificationProviders(),
    staleTime: 30_000,
  })
  if (!query.data) {
    return (
      <section className="space-y-4 pb-4">
        <h1 className="text-2xl font-semibold">Notification Delivery</h1>
        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardContent className="p-6 text-sm text-muted-foreground">
            Loading notification provider settings…
          </CardContent>
        </Card>
      </section>
    )
  }

  const dataSnapshotKey = [
    query.data.activeProvider,
    String(query.data.mailgun.enabled),
    query.data.mailgun.region,
    query.data.mailgun.domain,
    query.data.mailgun.fromAddress,
    query.data.mailgun.fromName ?? '',
    query.data.mailgun.replyToAddress ?? '',
    String(query.data.mailgun.hasApiKey),
  ].join('|')

  return <NotificationDeliverySettingsEditor key={dataSnapshotKey} data={query.data} onRefresh={() => query.refetch()} />
}

function NotificationDeliverySettingsEditor({
  data,
  onRefresh,
}: {
  data: Awaited<ReturnType<typeof fetchNotificationProviders>>
  onRefresh: () => Promise<unknown>
}) {
  const [draft, setDraft] = useState<DraftState>(() => toDraftState(data))

  const saveMutation = useMutation({
    mutationFn: async () => {
      await updateNotificationProviders({
        data: {
          activeProvider: draft.activeProvider,
          mailgun: {
            enabled: draft.mailgun.enabled,
            region: draft.mailgun.region,
            domain: draft.mailgun.domain,
            fromAddress: draft.mailgun.fromAddress,
            fromName: draft.mailgun.fromName,
            replyToAddress: draft.mailgun.replyToAddress,
            apiKey: draft.mailgun.apiKey,
          },
        },
      })
    },
    onSuccess: async () => {
      toast.success('Notification delivery settings saved')
      await onRefresh()
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to save notification delivery settings'))
    },
  })

  const validateMutation = useMutation({
    mutationFn: () => validateMailgunConfiguration(),
    onSuccess: (result) => {
      toast.success(result.message)
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Mailgun validation failed'))
    },
  })

  const testMutation = useMutation({
    mutationFn: () => sendMailgunTestEmail(),
    onSuccess: (result) => {
      toast.success(result.message)
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to send test email'))
    },
  })

  function updateDraft(mutator: (current: DraftState) => DraftState) {
    setDraft((current) => mutator(current))
  }

  return (
    <section className="space-y-5 pb-4">
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold">Notification Delivery</h1>
        <p className="max-w-3xl text-sm leading-6 text-muted-foreground">
          Configure outbound notification providers in one place. PatchHound can keep using SMTP, or you can switch delivery to Mailgun using a sending domain and API key.
        </p>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(22rem,1fr)]">
        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle>Provider Selection</CardTitle>
                <CardDescription>
                  Keep all notification providers in one admin area and choose which one is active.
                </CardDescription>
              </div>
              <Badge variant="outline" className="rounded-full">
                Active: {draft.activeProvider === 'mailgun' ? 'Mailgun' : 'SMTP'}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-3 md:grid-cols-2">
              <button
                type="button"
                onClick={() => updateDraft((current) => ({ ...current, activeProvider: 'smtp' }))}
                className={`rounded-2xl border px-4 py-4 text-left transition-colors ${draft.activeProvider === 'smtp' ? 'border-primary/40 bg-primary/8' : 'border-border/70 bg-background/30'}`}
              >
                <div className="flex items-center gap-2">
                  <ShieldCheck className="size-4 text-primary" />
                  <span className="font-medium">SMTP</span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">
                  Uses the server-side SMTP settings already deployed with PatchHound.
                </p>
              </button>
              <button
                type="button"
                onClick={() => updateDraft((current) => ({ ...current, activeProvider: 'mailgun' }))}
                className={`rounded-2xl border px-4 py-4 text-left transition-colors ${draft.activeProvider === 'mailgun' ? 'border-primary/40 bg-primary/8' : 'border-border/70 bg-background/30'}`}
              >
                <div className="flex items-center gap-2">
                  <Mail className="size-4 text-primary" />
                  <span className="font-medium">Mailgun</span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">
                  Official Mailgun Messages API using your sending domain and domain sending key.
                </p>
              </button>
            </div>

            <div className="rounded-2xl border border-border/70 bg-background/30 p-4 text-sm text-muted-foreground">
              <p className="font-medium text-foreground">Current SMTP fallback</p>
              <p className="mt-2">
                Host: {data.smtp.host}:{data.smtp.port}
              </p>
              <p>From: {data.smtp.fromAddress}</p>
              <p>Encryption: {data.smtp.enableSsl ? 'STARTTLS/SSL enabled' : 'No TLS upgrade configured'}</p>
            </div>
          </CardContent>
        </Card>

        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardHeader>
            <CardTitle>Mailgun Guidance</CardTitle>
            <CardDescription>
              PatchHound handles Mailgun’s Basic Auth username automatically as <code>api</code>.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 text-sm leading-6 text-muted-foreground">
            <p>Use a Mailgun domain sending key when possible. It limits the key to message sending for a single domain.</p>
            <p>Select the correct region for your account. Mailgun uses different API hosts for US and EU regions.</p>
            <p>Validation checks the configured domain against Mailgun before you rely on it for production notifications.</p>
          </CardContent>
        </Card>
      </div>

      <Card className="rounded-2xl border-border/70 bg-card/85">
        <CardHeader>
          <CardTitle>Mailgun Configuration</CardTitle>
          <CardDescription>
            These settings are stored centrally. The API key is kept in OpenBao and only used for outbound email delivery.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <label className="flex items-center gap-3 rounded-2xl border border-border/70 bg-background/30 px-4 py-3">
            <input
              type="checkbox"
              checked={draft.mailgun.enabled}
              onChange={(event) =>
                updateDraft((current) => ({
                  ...current,
                  mailgun: { ...current.mailgun, enabled: event.target.checked },
                }))
              }
            />
            <div>
              <p className="text-sm font-medium">Mailgun configured and allowed for delivery</p>
              <p className="text-xs text-muted-foreground">
                This must be on before Mailgun can be selected as the active delivery provider.
              </p>
            </div>
          </label>

          <div className="grid gap-4 md:grid-cols-2">
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Region</span>
              <select
                className="flex h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
                value={draft.mailgun.region}
                onChange={(event) =>
                  updateDraft((current) => ({
                    ...current,
                    mailgun: {
                      ...current.mailgun,
                      region: event.target.value === 'eu' ? 'eu' : 'us',
                    },
                  }))
                }
              >
                <option value="us">US API region</option>
                <option value="eu">EU API region</option>
              </select>
            </label>
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Sending Domain</span>
              <Input
                value={draft.mailgun.domain}
                placeholder="mg.example.com"
                onChange={(event) =>
                  updateDraft((current) => ({
                    ...current,
                    mailgun: { ...current.mailgun, domain: event.target.value },
                  }))
                }
              />
            </label>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">From Address</span>
              <Input
                value={draft.mailgun.fromAddress}
                placeholder="notifications@example.com"
                onChange={(event) =>
                  updateDraft((current) => ({
                    ...current,
                    mailgun: { ...current.mailgun, fromAddress: event.target.value },
                  }))
                }
              />
            </label>
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">From Name</span>
              <Input
                value={draft.mailgun.fromName}
                placeholder="PatchHound"
                onChange={(event) =>
                  updateDraft((current) => ({
                    ...current,
                    mailgun: { ...current.mailgun, fromName: event.target.value },
                  }))
                }
              />
            </label>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Reply-To Address</span>
              <Input
                value={draft.mailgun.replyToAddress}
                placeholder="security@example.com"
                onChange={(event) =>
                  updateDraft((current) => ({
                    ...current,
                    mailgun: { ...current.mailgun, replyToAddress: event.target.value },
                  }))
                }
              />
            </label>
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">API Key</span>
              <Input
                type="password"
                value={draft.mailgun.apiKey}
                placeholder={
                  draft.mailgun.hasApiKey
                    ? 'Stored in OpenBao. Enter a new key to rotate it.'
                    : 'Enter Mailgun domain sending key'
                }
                onChange={(event) =>
                  updateDraft((current) => ({
                    ...current,
                    mailgun: { ...current.mailgun, apiKey: event.target.value },
                  }))
                }
              />
            </label>
          </div>

          <div className="flex flex-wrap gap-3">
            <Button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}>
              {saveMutation.isPending ? 'Saving…' : 'Save notification settings'}
            </Button>
            <Button
              variant="outline"
              onClick={() => validateMutation.mutate()}
              disabled={validateMutation.isPending}
            >
              {validateMutation.isPending ? 'Validating…' : 'Validate Mailgun'}
            </Button>
            <Button
              variant="outline"
              onClick={() => testMutation.mutate()}
              disabled={testMutation.isPending}
            >
              <Send className="mr-2 size-4" />
              {testMutation.isPending ? 'Sending…' : 'Send test email'}
            </Button>
          </div>
        </CardContent>
      </Card>
    </section>
  )
}
