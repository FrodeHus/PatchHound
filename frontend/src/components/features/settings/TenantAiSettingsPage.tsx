import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Bot,
  CircleAlert,
  CircleHelp,
  Cloud,
  Cpu,
  KeyRound,
  PenSquare,
  Plus,
  Server,
  Sparkles,
} from 'lucide-react'
import {
  fetchTenantAiProfileModels,
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
import { InsetPanel } from '@/components/ui/inset-panel'
import { Separator } from '@/components/ui/separator'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Textarea } from '@/components/ui/textarea'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
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

const EMPTY_PROFILES: TenantAiProfile[] = []

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

function extractMissingOllamaModel(errorMessage: string, model: string) {
  if (!errorMessage || !model) {
    return null
  }

  return errorMessage.toLowerCase().includes('not found') && errorMessage.includes(model) ? model : null
}

export function TenantAiSettingsPage() {
  const queryClient = useQueryClient()
  const { selectedTenantId, tenants } = useTenantScope()
  const [sheetOpen, setSheetOpen] = useState(false)
  const [draft, setDraft] = useState<SaveTenantAiProfile>(createEmptyProfile)
  const [editingProfileId, setEditingProfileId] = useState<string | null>(null)

  const selectedTenantName = useMemo(
    () => tenants.find((item) => item.id === selectedTenantId)?.name ?? 'Current tenant',
    [selectedTenantId, tenants],
  )

  const profilesQuery = useQuery({
    queryKey: ['tenant-ai-profiles', selectedTenantId],
    queryFn: () => fetchTenantAiProfiles(),
    enabled: !!selectedTenantId,
  })

  const profiles = profilesQuery.data ?? EMPTY_PROFILES
  const editingProfile = useMemo(
    () => profiles.find((item) => item.id === editingProfileId) ?? null,
    [editingProfileId, profiles],
  )

  const saveMutation = useMutation({
    mutationFn: (payload: SaveTenantAiProfile) => saveTenantAiProfile({ data: payload }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tenant-ai-profiles', selectedTenantId] })
      setSheetOpen(false)
      setEditingProfileId(null)
      setDraft(createEmptyProfile())
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
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tenant-ai-profiles', selectedTenantId] })
    },
  })

  const modelsMutation = useMutation({
    mutationFn: (id: string) => fetchTenantAiProfileModels({ data: { id } }),
    onSuccess: (result) => {
      if (result.models.length === 1) {
        setDraft((current) => ({ ...current, model: result.models[0] }))
      }
    },
  })

  const defaultProfile = profiles.find((item) => item.isDefault) ?? null

  return (
    <TooltipProvider>
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

        <div className="grid gap-4 xl:grid-cols-[360px_minmax(0,1fr)]">
          <Card className="rounded-2xl border-border/70 bg-card/85">
            <CardHeader className="space-y-4">
              <div className="flex items-start justify-between gap-3">
                <div className="space-y-1">
                  <CardTitle>Profiles</CardTitle>
                  <p className="text-sm text-muted-foreground">
                    Keep one validated default profile active for report generation in this tenant.
                  </p>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setEditingProfileId(null)
                    setDraft(createEmptyProfile())
                    setSheetOpen(true)
                  }}
                >
                  <Plus className="size-4" />
                  New
                </Button>
              </div>
            </CardHeader>

            <CardContent className="space-y-3">
              {profilesQuery.isLoading ? (
                <p className="text-sm text-muted-foreground">Loading AI profiles...</p>
              ) : null}

              {!profilesQuery.isLoading && profiles.length === 0 ? (
                <InsetPanel className="flex flex-wrap items-center justify-between gap-4 px-4 py-4">
                  <div className="space-y-1">
                    <p className="font-medium text-foreground">No AI profile exists yet.</p>
                    <p className="text-sm text-muted-foreground">
                      Create and validate one before generating tenant AI reports.
                    </p>
                  </div>
                  <Button
                    type="button"
                    onClick={() => {
                      setEditingProfileId(null)
                      setDraft(createEmptyProfile())
                      setSheetOpen(true)
                    }}
                  >
                    <Plus className="size-4" />
                    Create profile
                  </Button>
                </InsetPanel>
              ) : null}

              <div className="space-y-2">
                {profiles.map((profile) => (
                  <InsetPanel key={profile.id} className="space-y-3 p-4">
                    <div className="flex items-start justify-between gap-3">
                      <div className="space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <p className="font-medium">{profile.name}</p>
                          {profile.isDefault ? <Badge>Default</Badge> : null}
                          {!profile.isEnabled ? <Badge variant="outline">Disabled</Badge> : null}
                          {profile.isDefault && profile.lastValidationStatus !== 'Valid' ? (
                            <Badge variant="destructive">Blocked</Badge>
                          ) : null}
                        </div>
                        <p className="text-sm text-muted-foreground">
                          {getProviderLabel(profile.providerType)} · {profile.model}
                        </p>
                      </div>
                      <Badge variant={getValidationVariant(profile.lastValidationStatus)}>
                        {profile.lastValidationStatus}
                      </Badge>
                    </div>

                    <div className="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
                      <span>{profile.hasSecret ? 'Secret saved' : 'No secret stored'}</span>
                      {profile.lastValidatedAt ? <span>Checked {formatDateTime(profile.lastValidatedAt)}</span> : null}
                    </div>

                    <div className="flex flex-wrap gap-2">
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() => {
                          setEditingProfileId(profile.id)
                          setDraft(toDraft(profile))
                          setSheetOpen(true)
                        }}
                      >
                        <PenSquare className="size-4" />
                        Edit
                      </Button>
                      {!profile.isDefault ? (
                        <Button
                          type="button"
                          variant="outline"
                          disabled={setDefaultMutation.isPending || !profile.isEnabled}
                          onClick={() => setDefaultMutation.mutate(profile.id)}
                        >
                          Set default
                        </Button>
                      ) : null}
                      <Button
                        type="button"
                        variant="outline"
                        disabled={validateMutation.isPending}
                        onClick={() => validateMutation.mutate(profile.id)}
                      >
                        {validateMutation.isPending && editingProfileId === profile.id ? 'Validating...' : 'Validate'}
                      </Button>
                    </div>
                  </InsetPanel>
                ))}
              </div>
            </CardContent>
          </Card>

          <div className="space-y-4">
            <Card className="rounded-2xl border-border/70 bg-card/75">
              <CardContent className="grid gap-4 p-6 md:grid-cols-3">
                <StatusTile label="Default profile" value={defaultProfile?.name ?? 'Not configured'} />
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

            <Card className="rounded-2xl border-border/70 bg-card/75">
              <CardHeader>
                <CardTitle>Tenant AI posture</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3 text-sm text-muted-foreground">
                <p>
                  AI report generation uses the tenant&apos;s default validated profile. Keep one stable
                  production profile and use additional profiles for testing or alternate report styles.
                </p>
                <InsetPanel emphasis="subtle" className="px-4 py-3">
                  Default status:{' '}
                  <span className="font-medium text-foreground">
                    {defaultProfile
                      ? `${defaultProfile.name} · ${defaultProfile.lastValidationStatus}`
                      : 'No default profile configured'}
                  </span>
                </InsetPanel>
              </CardContent>
            </Card>
          </div>
        </div>

        <AiProfileSheet
          open={sheetOpen}
          profile={editingProfile}
          draft={draft}
          isSaving={saveMutation.isPending}
          saveError={saveMutation.isError ? getErrorMessage(saveMutation.error, 'Failed to save AI profile.') : null}
          validateError={validateMutation.isError ? getErrorMessage(validateMutation.error, 'Failed to validate AI profile.') : null}
          isValidating={validateMutation.isPending}
          onValidate={(id) => validateMutation.mutate(id)}
          modelResult={modelsMutation.data ?? null}
          modelError={modelsMutation.isError ? getErrorMessage(modelsMutation.error, 'Failed to list available models.') : null}
          isListingModels={modelsMutation.isPending}
          onListModels={(id) => modelsMutation.mutate(id)}
          onOpenChange={(open) => {
            setSheetOpen(open)
            if (!open) {
              setEditingProfileId(null)
              setDraft(createEmptyProfile())
            }
          }}
          onDraftChange={setDraft}
          onSave={() => saveMutation.mutate(draft)}
        />
      </section>
    </TooltipProvider>
  )
}

function AiProfileSheet({
  open,
  profile,
  draft,
  isSaving,
  saveError,
  validateError,
  isValidating,
  onValidate,
  modelResult,
  modelError,
  isListingModels,
  onListModels,
  onOpenChange,
  onDraftChange,
  onSave,
}: {
  open: boolean
  profile: TenantAiProfile | null
  draft: SaveTenantAiProfile
  isSaving: boolean
  saveError: string | null
  validateError: string | null
  isValidating: boolean
  onValidate: (id: string) => void
  modelResult: { id: string; models: string[] } | null
  modelError: string | null
  isListingModels: boolean
  onListModels: (id: string) => void
  onOpenChange: (open: boolean) => void
  onDraftChange: React.Dispatch<React.SetStateAction<SaveTenantAiProfile>>
  onSave: () => void
}) {
  const saveLabel = profile ? 'Save changes' : 'Create profile'

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full sm:max-w-3xl">
        <SheetHeader className="border-b border-border/60">
          <SheetTitle>{profile ? 'Edit AI profile' : 'Create AI profile'}</SheetTitle>
          <SheetDescription>
            Configure provider, connection, runtime, and prompt settings for this tenant AI profile.
          </SheetDescription>
        </SheetHeader>

        <div className="flex-1 space-y-6 overflow-y-auto p-5">
          <FormSection title="Provider" icon={Sparkles}>
            <div className="grid gap-3 md:grid-cols-3">
              {providerOptions.map((provider) => {
                const Icon = provider.icon
                const active = draft.providerType === provider.type
                return (
                  <button
                    key={provider.type}
                    type="button"
                    className={`rounded-xl border px-4 py-3 text-left transition-colors ${
                      active
                        ? 'border-primary/40 bg-primary/8'
                        : 'border-border/70 bg-background hover:border-foreground/15'
                    }`}
                    onClick={() => {
                      onDraftChange((current) => ({
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
          </FormSection>

          <FormSection title="Identity" icon={Bot}>
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Profile name" tooltip="Operator-facing name for this tenant AI profile.">
                <Input
                  value={draft.name}
                  onChange={(event) => onDraftChange((current) => ({ ...current, name: event.target.value }))}
                />
              </Field>
              <Field label="Model" tooltip="Exact model or deployment name used for report generation.">
                <div className="space-y-3">
                  <div className="flex gap-2">
                    <Input
                      value={draft.model}
                      onChange={(event) => onDraftChange((current) => ({ ...current, model: event.target.value }))}
                    />
                    {profile ? (
                      <Button
                        type="button"
                        variant="outline"
                        disabled={isListingModels}
                        onClick={() => onListModels(profile.id)}
                      >
                        {isListingModels ? 'Listing...' : 'List models'}
                      </Button>
                    ) : null}
                  </div>

                  {(() => {
                    const currentModelResult = modelResult?.id === profile?.id ? modelResult : null

                    return (
                      <>
                        {currentModelResult && currentModelResult.models.length > 0 ? (
                          <div className="flex flex-wrap gap-2">
                            {currentModelResult.models.map((model) => (
                              <button
                                key={model}
                                type="button"
                                className={`rounded-full border px-3 py-1 text-xs transition-colors ${
                                  draft.model === model
                                    ? 'border-primary bg-primary text-primary-foreground'
                                    : 'border-border bg-background text-foreground hover:bg-muted'
                                }`}
                                onClick={() => onDraftChange((current) => ({ ...current, model }))}
                              >
                                {model}
                              </button>
                            ))}
                          </div>
                        ) : null}

                        {currentModelResult && currentModelResult.models.length === 0 ? (
                          <p className="text-xs text-muted-foreground">No models were returned by this provider.</p>
                        ) : null}
                      </>
                    )
                  })()}

                  {modelError ? <p className="text-xs text-destructive">{modelError}</p> : null}

                  {profile?.lastValidationStatus === 'Invalid' &&
                  draft.providerType === 'Ollama' &&
                  extractMissingOllamaModel(profile.lastValidationError, draft.model) ? (
                    <div className="rounded-[16px] border border-amber-300/25 bg-amber-500/8 px-3 py-2 text-xs text-amber-900 dark:text-amber-200">
                      Ollama could not find <code>{draft.model}</code>. Run <code>ollama pull {draft.model}</code> on the Ollama host, then validate again.
                    </div>
                  ) : null}
                </div>
              </Field>
            </div>
          </FormSection>

          <FormSection title="Connection" icon={KeyRound}>
            <div className="grid gap-4 md:grid-cols-2">
              <Field
                label={draft.providerType === 'AzureOpenAi' ? 'Endpoint' : 'Base URL'}
                tooltip={
                  draft.providerType === 'AzureOpenAi'
                    ? 'Azure OpenAI resource endpoint.'
                    : draft.providerType === 'Ollama'
                      ? 'Reachable Ollama server root, for example http://localhost:11434.'
                      : 'Provider base URL. Defaults to the OpenAI API endpoint.'
                }
              >
                <Input
                  value={draft.baseUrl}
                  onChange={(event) => onDraftChange((current) => ({ ...current, baseUrl: event.target.value }))}
                />
              </Field>

              {draft.providerType === 'AzureOpenAi' ? (
                <>
                  <Field label="Deployment name" tooltip="Azure deployment name configured for the selected model.">
                    <Input
                      value={draft.deploymentName}
                      onChange={(event) => onDraftChange((current) => ({ ...current, deploymentName: event.target.value }))}
                    />
                  </Field>
                  <Field label="API version" tooltip="Azure OpenAI API version used for requests.">
                    <Input
                      value={draft.apiVersion}
                      onChange={(event) => onDraftChange((current) => ({ ...current, apiVersion: event.target.value }))}
                    />
                  </Field>
                </>
              ) : null}

              {draft.providerType === 'Ollama' ? (
                <Field label="Keep alive" tooltip="Optional Ollama keep-alive value, for example 5m.">
                  <Input
                    value={draft.keepAlive}
                    onChange={(event) => onDraftChange((current) => ({ ...current, keepAlive: event.target.value }))}
                  />
                </Field>
              ) : null}

              {draft.providerType !== 'Ollama' ? (
                <Field
                  label="API key"
                  tooltip={
                    profile ? 'Leave blank to retain the currently stored secret.' : 'Stored server-side and not shown again after save.'
                  }
                >
                  <Input
                    type="password"
                    placeholder={profile ? 'Leave blank to keep the current secret' : ''}
                    value={draft.apiKey}
                    onChange={(event) => onDraftChange((current) => ({ ...current, apiKey: event.target.value }))}
                  />
                </Field>
              ) : (
                <InsetPanel className="px-4 py-3 text-sm text-muted-foreground md:col-span-2">
                  Ollama typically does not require an API key. If you front it with a secured gateway later, secret support is already in place.
                </InsetPanel>
              )}
            </div>
          </FormSection>

          <FormSection title="Runtime controls" icon={Cpu}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Temperature" tooltip="Controls output variation. Lower values are more deterministic.">
                <Input
                  type="number"
                  step="0.1"
                  min="0"
                  max="2"
                  value={String(draft.temperature)}
                  onChange={(event) =>
                    onDraftChange((current) => ({
                      ...current,
                      temperature: Number(event.target.value || 0),
                    }))
                  }
                />
              </Field>
              <Field label="Top P" tooltip="Optional nucleus sampling control. Keep at 1 unless you need tighter output shaping.">
                <Input
                  type="number"
                  step="0.1"
                  min="0"
                  max="1"
                  value={draft.topP ?? ''}
                  onChange={(event) =>
                    onDraftChange((current) => ({
                      ...current,
                      topP: event.target.value === '' ? null : Number(event.target.value),
                    }))
                  }
                />
              </Field>
              <Field label="Max output tokens" tooltip="Upper bound for generated response length.">
                <Input
                  type="number"
                  min="1"
                  value={String(draft.maxOutputTokens)}
                  onChange={(event) =>
                    onDraftChange((current) => ({
                      ...current,
                      maxOutputTokens: Number(event.target.value || 0),
                    }))
                  }
                />
              </Field>
              <Field label="Timeout seconds" tooltip="Maximum time allowed for model responses before the request fails.">
                <Input
                  type="number"
                  min="1"
                  value={String(draft.timeoutSeconds)}
                  onChange={(event) =>
                    onDraftChange((current) => ({
                      ...current,
                      timeoutSeconds: Number(event.target.value || 0),
                    }))
                  }
                />
              </Field>
            </div>
          </FormSection>

          <FormSection title="System prompt" icon={Sparkles}>
            <div className="space-y-4">
              <div className="flex justify-end">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => onDraftChange((current) => ({ ...current, systemPrompt: recommendedPrompt }))}
                >
                  Use recommended prompt
                </Button>
              </div>
              <Textarea
                value={draft.systemPrompt}
                onChange={(event) => onDraftChange((current) => ({ ...current, systemPrompt: event.target.value }))}
                className="min-h-48"
              />
            </div>
          </FormSection>

          <FormSection title="Profile state" icon={Bot}>
            <div className="flex flex-wrap gap-6">
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={draft.isEnabled}
                  onCheckedChange={(checked) =>
                    onDraftChange((current) => ({
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
                    onDraftChange((current) => ({ ...current, isDefault: checked === true && current.isEnabled }))
                  }
                />
                Default profile
              </label>
            </div>
          </FormSection>

          {saveError ? <InlineError message={saveError} /> : null}
          {validateError ? <InlineError message={validateError} /> : null}
        </div>

        <SheetFooter className="border-t border-border/60">
          <div className="flex flex-wrap items-center gap-3">
            <Button type="button" disabled={isSaving} onClick={onSave}>
              {isSaving ? 'Saving...' : saveLabel}
            </Button>
            {profile ? (
              <Button
                type="button"
                variant="outline"
                disabled={isValidating}
                onClick={() => onValidate(profile.id)}
              >
                {isValidating ? 'Validating...' : 'Validate connection'}
              </Button>
            ) : null}
          </div>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  )
}

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

function FormSection({
  title,
  icon: Icon,
  children,
}: {
  title: string
  icon: React.ComponentType<{ className?: string }>
  children: React.ReactNode
}) {
  return (
    <section className="space-y-4">
      <div className="flex items-center gap-2 text-sm font-medium">
        <Icon className="size-4 text-primary" />
        {title}
      </div>
      {children}
      <Separator />
    </section>
  )
}

function Field({
  label,
  tooltip,
  children,
}: {
  label: string
  tooltip?: string
  children: React.ReactNode
}) {
  return (
    <div className="grid content-start gap-2">
      <div className="flex min-h-5 items-center gap-2">
        <label className="text-sm font-medium">{label}</label>
        {tooltip ? (
          <Tooltip>
            <TooltipTrigger className="inline-flex items-center text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
              <CircleHelp className="size-3.5" />
            </TooltipTrigger>
            <TooltipContent
              align="start"
              className="max-w-sm rounded-lg border border-border/80 bg-popover px-3 py-2 text-xs leading-5 text-popover-foreground shadow-lg"
            >
              {tooltip}
            </TooltipContent>
          </Tooltip>
        ) : null}
      </div>
      {children}
    </div>
  )
}
