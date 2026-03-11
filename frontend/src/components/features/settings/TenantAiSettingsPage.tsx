import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Bot,
  CircleAlert,
  Cloud,
  Cpu,
  KeyRound,
  Server,
  Sparkles,
} from 'lucide-react'
import {
  fetchTenantAiProfiles,
  saveTenantAiProfile,
  setDefaultTenantAiProfile,
  validateTenantAiProfile,
} from '@/api/ai-settings.functions'
import type { SaveTenantAiProfile, TenantAiProfile } from '@/api/ai-settings.schemas'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { Textarea } from '@/components/ui/textarea'
import { formatDateTime } from '@/lib/formatting'

const recommendedPrompt = `You are a PatchHound vulnerability analysis assistant.
Use only the vulnerability and tenant asset context provided.
Do not invent facts that are not present in the input.
Prioritize exploitability, blast radius, asset criticality, and remediation order.
Return concise markdown with these sections:
- Executive Summary
- Technical Analysis
- Affected Tenant Context
- Prioritized Actions`

const providerOptions = [
  {
    type: 'Ollama',
    label: 'Ollama',
    hint: 'Local or self-hosted model runtime.',
    icon: Server,
  },
  {
    type: 'AzureOpenAi',
    label: 'Azure OpenAI',
    hint: 'Enterprise-hosted deployment and API version.',
    icon: Cloud,
  },
  {
    type: 'OpenAi',
    label: 'OpenAI',
    hint: 'Direct OpenAI API or compatible gateway.',
    icon: Sparkles,
  },
] as const satisfies ReadonlyArray<{
  type: SaveTenantAiProfile['providerType']
  label: string
  hint: string
  icon: React.ComponentType<{ className?: string }>
}>

const providerDefaults: Record<SaveTenantAiProfile['providerType'], Partial<SaveTenantAiProfile>> = {
  Ollama: {
    baseUrl: 'http://localhost:11434',
    model: 'llama3.1:8b',
    keepAlive: '5m',
    apiVersion: '',
    deploymentName: '',
  },
  AzureOpenAi: {
    baseUrl: '',
    model: 'gpt-4o',
    apiVersion: '2024-10-21',
    deploymentName: '',
    keepAlive: '',
  },
  OpenAi: {
    baseUrl: 'https://api.openai.com/v1',
    model: 'gpt-4.1-mini',
    apiVersion: '',
    deploymentName: '',
    keepAlive: '',
  },
}

function createEmptyProfile(): SaveTenantAiProfile {
  return {
    name: 'Default analysis',
    providerType: 'OpenAi',
    isDefault: true,
    isEnabled: true,
    model: 'gpt-4.1-mini',
    systemPrompt: recommendedPrompt,
    temperature: 0.2,
    topP: 1,
    maxOutputTokens: 1200,
    timeoutSeconds: 60,
    baseUrl: 'https://api.openai.com/v1',
    deploymentName: '',
    apiVersion: '',
    keepAlive: '',
    apiKey: '',
  }
}

function toDraft(profile: TenantAiProfile): SaveTenantAiProfile {
  return {
    id: profile.id,
    name: profile.name,
    providerType: profile.providerType as SaveTenantAiProfile['providerType'],
    isDefault: profile.isDefault,
    isEnabled: profile.isEnabled,
    model: profile.model,
    systemPrompt: profile.systemPrompt,
    temperature: profile.temperature,
    topP: profile.topP,
    maxOutputTokens: profile.maxOutputTokens,
    timeoutSeconds: profile.timeoutSeconds,
    baseUrl: profile.baseUrl,
    deploymentName: profile.deploymentName,
    apiVersion: profile.apiVersion,
    keepAlive: profile.keepAlive,
    apiKey: '',
  }
}

function getProviderLabel(providerType: string) {
  return providerOptions.find((item) => item.type === providerType)?.label ?? providerType
}

function getValidationVariant(status: string): 'default' | 'outline' | 'destructive' | 'secondary' {
  switch (status) {
    case 'Valid':
      return 'default'
    case 'Invalid':
      return 'destructive'
    default:
      return 'outline'
  }
}

function getErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message.trim()) {
    return error.message
  }

  return fallback
}

export function TenantAiSettingsPage() {
  const queryClient = useQueryClient()
  const { selectedTenantId, tenants } = useTenantScope()
  const selectedTenantName = useMemo(
    () => tenants.find((item) => item.id === selectedTenantId)?.name ?? 'Current tenant',
    [selectedTenantId, tenants],
  )

  const profilesQuery = useQuery({
    queryKey: ['tenant-ai-profiles', selectedTenantId],
    queryFn: () => fetchTenantAiProfiles(),
    enabled: !!selectedTenantId,
  })

  const [draft, setDraft] = useState<SaveTenantAiProfile>(createEmptyProfile)
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null)

  const saveMutation = useMutation({
    mutationFn: (payload: SaveTenantAiProfile) => saveTenantAiProfile({ data: payload }),
    onSuccess: async (profile) => {
      setSelectedProfileId(profile.id)
      setDraft(toDraft(profile))
      await queryClient.invalidateQueries({ queryKey: ['tenant-ai-profiles', selectedTenantId] })
    },
  })

  const validateMutation = useMutation({
    mutationFn: (id: string) => validateTenantAiProfile({ data: { id } }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tenant-ai-profiles', selectedTenantId] })
    },
  })

  const setDefaultMutation = useMutation({
    mutationFn: (id: string) => setDefaultTenantAiProfile({ data: { id } }),
    onSuccess: async (profile) => {
      setSelectedProfileId(profile.id)
      setDraft(toDraft(profile))
      await queryClient.invalidateQueries({ queryKey: ['tenant-ai-profiles', selectedTenantId] })
    },
  })

  const profiles = profilesQuery.data ?? EMPTY_PROFILES
  const selectedProfile = useMemo(
    () => profiles.find((item) => item.id === selectedProfileId) ?? null,
    [profiles, selectedProfileId],
  )

  const activeProvider = providerOptions.find((item) => item.type === draft.providerType) ?? providerOptions[2]

  return (
    <section className="space-y-5 pb-6">
      <header className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="outline">Tenant settings</Badge>
          <Badge variant="secondary">{selectedTenantName}</Badge>
        </div>
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold tracking-tight">AI Configuration</h1>
          <p className="max-w-3xl text-sm leading-6 text-muted-foreground">
            Configure the default analysis model, prompt, and runtime controls for this tenant.
            Validation stays explicit so you can test provider reachability before using it for AI
            reports.
          </p>
        </div>
      </header>

      <div className="grid gap-4 xl:grid-cols-[320px_minmax(0,1fr)]">
        <Card className="rounded-[28px] border-border/70 bg-card/85">
          <CardHeader className="space-y-4">
            <div className="flex items-start justify-between gap-3">
              <div className="space-y-1">
                <CardTitle>Profiles</CardTitle>
                <p className="text-sm text-muted-foreground">
                  One default profile is used for AI reports in this tenant.
                </p>
              </div>
              <Button
                type="button"
                variant="outline"
                onClick={() => {
                  setSelectedProfileId(null)
                  setDraft(createEmptyProfile())
                }}
              >
                New
              </Button>
            </div>
          </CardHeader>

          <CardContent className="space-y-3">
            {profilesQuery.isLoading ? (
              <p className="text-sm text-muted-foreground">Loading AI profiles...</p>
            ) : null}

            {!profilesQuery.isLoading && profiles.length === 0 ? (
              <div className="rounded-2xl border border-dashed border-border bg-muted/30 p-4 text-sm text-muted-foreground">
                No AI profile exists yet. Create one and validate it before generating tenant AI
                reports.
              </div>
            ) : null}

            <div className="space-y-2">
              {profiles.map((profile) => (
                <button
                  key={profile.id}
                  type="button"
                  className={`w-full rounded-[22px] border px-4 py-3 text-left transition-colors ${
                    selectedProfileId === profile.id
                      ? 'border-primary/40 bg-primary/8'
                      : 'border-border/70 bg-background hover:border-foreground/15'
                  }`}
                  onClick={() => {
                    setSelectedProfileId(profile.id)
                    setDraft(toDraft(profile))
                  }}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="space-y-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="font-medium">{profile.name}</p>
                        {profile.isDefault ? <Badge>Default</Badge> : null}
                        {!profile.isEnabled ? <Badge variant="outline">Disabled</Badge> : null}
                      </div>
                      <p className="text-sm text-muted-foreground">
                        {getProviderLabel(profile.providerType)} · {profile.model}
                      </p>
                    </div>
                    <Badge variant={getValidationVariant(profile.lastValidationStatus)}>
                      {profile.lastValidationStatus}
                    </Badge>
                  </div>

                  <div className="mt-3 flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
                    <span>{profile.hasSecret ? 'Secret saved' : 'No secret stored'}</span>
                    {profile.lastValidatedAt ? (
                      <span>Checked {formatDateTime(profile.lastValidatedAt)}</span>
                    ) : null}
                  </div>
                </button>
              ))}
            </div>
          </CardContent>
        </Card>

        <div className="space-y-4">
          <Card className="rounded-[28px] border-border/70 bg-card">
            <CardHeader className="space-y-4">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="space-y-1">
                  <CardTitle>{selectedProfileId ? 'Edit AI profile' : 'Create AI profile'}</CardTitle>
                  <p className="text-sm text-muted-foreground">
                    Keep one validated default profile active for the tenant. Use additional profiles
                    for testing or alternate report styles.
                  </p>
                </div>

                <div className="flex flex-wrap items-center gap-2">
                  {selectedProfile ? (
                    <Badge variant={getValidationVariant(selectedProfile.lastValidationStatus)}>
                      {selectedProfile.lastValidationStatus}
                    </Badge>
                  ) : null}
                  {selectedProfile?.isDefault ? <Badge>Default</Badge> : null}
                </div>
              </div>

              <div className="grid gap-3 md:grid-cols-3">
                {providerOptions.map((provider) => {
                  const Icon = provider.icon
                  const active = draft.providerType === provider.type
                  return (
                    <button
                      key={provider.type}
                      type="button"
                      className={`rounded-[22px] border px-4 py-3 text-left transition-colors ${
                        active
                          ? 'border-primary/40 bg-primary/8'
                          : 'border-border/70 bg-background hover:border-foreground/15'
                      }`}
                      onClick={() => {
                        setDraft((current) => ({
                          ...current,
                          providerType: provider.type,
                          ...providerDefaults[provider.type],
                        }))
                      }}
                    >
                      <div className="flex items-center gap-2">
                        <Icon className="size-4 text-primary" />
                        <span className="font-medium">{provider.label}</span>
                      </div>
                      <p className="mt-2 text-sm leading-5 text-muted-foreground">{provider.hint}</p>
                    </button>
                  )
                })}
              </div>
            </CardHeader>

            <CardContent className="space-y-6">
              <section className="space-y-4">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <Bot className="size-4 text-primary" />
                  Identity
                </div>

                <div className="grid gap-4 md:grid-cols-2">
                  <Field label="Profile name">
                    <Input
                      value={draft.name}
                      onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))}
                    />
                  </Field>
                  <Field label="Model">
                    <Input
                      value={draft.model}
                      onChange={(event) => setDraft((current) => ({ ...current, model: event.target.value }))}
                    />
                  </Field>
                </div>
              </section>

              <Separator />

              <section className="space-y-4">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <KeyRound className="size-4 text-primary" />
                  Connection
                </div>

                <div className="grid gap-4 md:grid-cols-2">
                  <Field label={draft.providerType === 'AzureOpenAi' ? 'Endpoint' : 'Base URL'}>
                    <Input
                      value={draft.baseUrl}
                      onChange={(event) => setDraft((current) => ({ ...current, baseUrl: event.target.value }))}
                    />
                  </Field>

                  {draft.providerType === 'AzureOpenAi' ? (
                    <>
                      <Field label="Deployment name">
                        <Input
                          value={draft.deploymentName}
                          onChange={(event) =>
                            setDraft((current) => ({ ...current, deploymentName: event.target.value }))
                          }
                        />
                      </Field>
                      <Field label="API version">
                        <Input
                          value={draft.apiVersion}
                          onChange={(event) =>
                            setDraft((current) => ({ ...current, apiVersion: event.target.value }))
                          }
                        />
                      </Field>
                    </>
                  ) : null}

                  {draft.providerType === 'Ollama' ? (
                    <Field label="Keep alive">
                      <Input
                        value={draft.keepAlive}
                        onChange={(event) => setDraft((current) => ({ ...current, keepAlive: event.target.value }))}
                      />
                    </Field>
                  ) : null}

                  {draft.providerType !== 'Ollama' ? (
                    <Field label={selectedProfileId ? 'API key' : 'API key'}>
                      <Input
                        type="password"
                        placeholder={selectedProfileId ? 'Leave blank to keep the current secret' : ''}
                        value={draft.apiKey}
                        onChange={(event) => setDraft((current) => ({ ...current, apiKey: event.target.value }))}
                      />
                      <p className="text-xs text-muted-foreground">
                        {selectedProfileId
                          ? 'Leave this empty to retain the stored secret.'
                          : 'Stored server-side and never shown again after save.'}
                      </p>
                    </Field>
                  ) : (
                    <div className="rounded-[22px] border border-dashed border-border bg-muted/25 px-4 py-3 text-sm text-muted-foreground md:col-span-2">
                      Ollama typically does not require an API key. If you front it with a secured
                      gateway later, secret support is already in place.
                    </div>
                  )}
                </div>
              </section>

              <Separator />

              <section className="space-y-4">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <Cpu className="size-4 text-primary" />
                  Runtime controls
                </div>

                <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                  <Field label="Temperature">
                    <Input
                      type="number"
                      step="0.1"
                      min="0"
                      max="2"
                      value={String(draft.temperature)}
                      onChange={(event) =>
                        setDraft((current) => ({
                          ...current,
                          temperature: Number(event.target.value || 0),
                        }))
                      }
                    />
                  </Field>
                  <Field label="Top P">
                    <Input
                      type="number"
                      step="0.1"
                      min="0"
                      max="1"
                      value={draft.topP ?? ''}
                      onChange={(event) =>
                        setDraft((current) => ({
                          ...current,
                          topP: event.target.value === '' ? null : Number(event.target.value),
                        }))
                      }
                    />
                  </Field>
                  <Field label="Max output tokens">
                    <Input
                      type="number"
                      min="1"
                      value={String(draft.maxOutputTokens)}
                      onChange={(event) =>
                        setDraft((current) => ({
                          ...current,
                          maxOutputTokens: Number(event.target.value || 0),
                        }))
                      }
                    />
                  </Field>
                  <Field label="Timeout seconds">
                    <Input
                      type="number"
                      min="1"
                      value={String(draft.timeoutSeconds)}
                      onChange={(event) =>
                        setDraft((current) => ({
                          ...current,
                          timeoutSeconds: Number(event.target.value || 0),
                        }))
                      }
                    />
                  </Field>
                </div>
              </section>

              <Separator />

              <section className="space-y-4">
                <div className="flex items-center justify-between gap-4">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2 text-sm font-medium">
                      <Sparkles className="size-4 text-primary" />
                      System prompt
                    </div>
                    <p className="text-sm text-muted-foreground">
                      This prompt defines the report style and analysis constraints for {activeProvider.label}.
                    </p>
                  </div>

                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setDraft((current) => ({ ...current, systemPrompt: recommendedPrompt }))}
                  >
                    Use recommended prompt
                  </Button>
                </div>

                <Textarea
                  value={draft.systemPrompt}
                  onChange={(event) => setDraft((current) => ({ ...current, systemPrompt: event.target.value }))}
                  className="min-h-48"
                />
              </section>

              <Separator />

              <section className="flex flex-wrap gap-6">
                <label className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={draft.isEnabled}
                    onCheckedChange={(checked) =>
                      setDraft((current) => ({
                        ...current,
                        isEnabled: checked === true,
                        isDefault: checked === true ? current.isDefault : false,
                      }))
                    }
                  />
                  Enabled for this tenant
                </label>

                <label className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={draft.isDefault}
                    disabled={!draft.isEnabled}
                    onCheckedChange={(checked) =>
                      setDraft((current) => ({ ...current, isDefault: checked === true && current.isEnabled }))
                    }
                  />
                  Default profile
                </label>
              </section>

              <div className="flex flex-wrap items-center gap-3">
                <Button type="button" disabled={saveMutation.isPending} onClick={() => saveMutation.mutate(draft)}>
                  {saveMutation.isPending ? 'Saving...' : 'Save profile'}
                </Button>

                {selectedProfileId ? (
                  <>
                    <Button
                      type="button"
                      variant="outline"
                      disabled={validateMutation.isPending}
                      onClick={() => validateMutation.mutate(selectedProfileId)}
                    >
                      {validateMutation.isPending ? 'Validating...' : 'Validate connection'}
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      disabled={setDefaultMutation.isPending || !draft.isEnabled}
                      onClick={() => setDefaultMutation.mutate(selectedProfileId)}
                    >
                      Set as default
                    </Button>
                  </>
                ) : null}
              </div>

              {saveMutation.isError ? (
                <InlineError message={getErrorMessage(saveMutation.error, 'Failed to save AI profile.')} />
              ) : null}
              {validateMutation.isError ? (
                <InlineError message={getErrorMessage(validateMutation.error, 'Failed to validate AI profile.')} />
              ) : null}
              {setDefaultMutation.isError ? (
                <InlineError
                  message={getErrorMessage(setDefaultMutation.error, 'Failed to update the default profile.')}
                />
              ) : null}
            </CardContent>
          </Card>

          <Card className="rounded-[28px] border-border/70 bg-card/75">
            <CardContent className="grid gap-4 p-6 md:grid-cols-3">
              <StatusTile
                label="Default profile"
                value={profiles.find((item) => item.isDefault)?.name ?? 'Not configured'}
              />
              <StatusTile
                label="Validated"
                value={String(profiles.filter((item) => item.lastValidationStatus === 'Valid').length)}
              />
              <StatusTile
                label="Enabled profiles"
                value={String(profiles.filter((item) => item.isEnabled).length)}
              />
            </CardContent>
          </Card>
        </div>
      </div>
    </section>
  )
}

const EMPTY_PROFILES: TenantAiProfile[] = []

function InlineError({ message }: { message: string }) {
  return (
    <div className="flex items-start gap-2 rounded-[18px] border border-destructive/25 bg-destructive/8 px-4 py-3 text-sm text-destructive">
      <CircleAlert className="mt-0.5 size-4 shrink-0" />
      <p>{message}</p>
    </div>
  )
}

function StatusTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="space-y-1">
      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="text-lg font-semibold tracking-tight">{value}</p>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid gap-2">
      <label className="text-sm font-medium">{label}</label>
      {children}
    </div>
  )
}
