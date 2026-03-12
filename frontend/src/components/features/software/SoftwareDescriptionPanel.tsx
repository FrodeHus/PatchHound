import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Bot, CircleAlert, CircleCheckBig, FileText } from 'lucide-react'
import { fetchTenantAiProfiles } from '@/api/ai-settings.functions'
import { generateTenantSoftwareDescription } from '@/api/software.functions'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import { formatDateTime } from '@/lib/formatting'

type SoftwareDescriptionPanelProps = {
  tenantSoftwareId: string
  initialDescription: string | null
  initialGeneratedAt: string | null
  initialProviderType: string | null
  initialProfileName: string | null
  initialModel: string | null
}

export function SoftwareDescriptionPanel({
  tenantSoftwareId,
  initialDescription,
  initialGeneratedAt,
  initialProviderType,
  initialProfileName,
  initialModel,
}: SoftwareDescriptionPanelProps) {
  const { selectedTenantId } = useTenantScope()
  const [description, setDescription] = useState(initialDescription)
  const [generatedAt, setGeneratedAt] = useState(initialGeneratedAt)
  const [providerType, setProviderType] = useState(initialProviderType)
  const [profileName, setProfileName] = useState(initialProfileName)
  const [model, setModel] = useState(initialModel)

  const profilesQuery = useQuery({
    queryKey: ['tenant-ai-profiles', selectedTenantId],
    queryFn: () => fetchTenantAiProfiles(),
    enabled: !!selectedTenantId,
  })
  const defaultProfile =
    (profilesQuery.data ?? []).find((profile) => profile.isDefault && profile.isEnabled) ?? null
  const defaultProfileIsUsable = defaultProfile?.lastValidationStatus === 'Valid'

  const mutation = useMutation({
    mutationFn: () =>
      generateTenantSoftwareDescription({
        data: {
          id: tenantSoftwareId,
        },
      }),
    onSuccess: (result) => {
      setDescription(result.description)
      setGeneratedAt(result.generatedAt)
      setProviderType(result.providerType)
      setProfileName(result.profileName)
      setModel(result.model)
    },
  })

  const canGenerate = !profilesQuery.isLoading && !!defaultProfile && defaultProfileIsUsable

  return (
    <section className="rounded-2xl border border-border/70 bg-card p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <FileText className="size-4 text-primary" />
            <h2 className="text-lg font-semibold">Product description</h2>
          </div>
          <p className="text-sm text-muted-foreground">
            Product-level description shared across observed versions for this normalized software.
          </p>
        </div>
        <Button
          type="button"
          size="sm"
          disabled={mutation.isPending || !canGenerate}
          onClick={() => mutation.mutate()}
        >
          {mutation.isPending
            ? 'Generating...'
            : description
              ? 'Regenerate'
              : 'Generate description'}
        </Button>
      </div>

      {description ? (
        <div className="mt-4 space-y-3">
          {generatedAt && providerType && profileName && model ? (
            <div className="flex flex-wrap items-center gap-2">
              <Badge>{profileName}</Badge>
              <Badge variant="outline">{providerType}</Badge>
              <Badge variant="outline">{model}</Badge>
              <Badge variant="outline">{formatDateTime(generatedAt)}</Badge>
            </div>
          ) : null}
          <MarkdownViewer content={description} />
        </div>
      ) : (
        <div className="mt-4 rounded-xl border border-dashed border-border bg-muted/20 p-4">
          <p className="text-sm text-muted-foreground">
            No product description has been generated yet.
          </p>
        </div>
      )}

      {defaultProfile && !defaultProfileIsUsable ? (
        <div className="mt-4 flex items-start gap-2 rounded-xl border border-amber-300/25 bg-amber-500/8 px-3 py-2 text-sm text-amber-900 dark:text-amber-200">
          <CircleAlert className="mt-0.5 size-4 shrink-0" />
          <p>Product description generation is disabled until the default AI profile validates successfully.</p>
        </div>
      ) : null}

      {mutation.isSuccess ? (
        <div className="mt-4 flex items-start gap-2 rounded-xl border border-emerald-300/25 bg-emerald-500/8 px-3 py-2 text-sm text-emerald-900 dark:text-emerald-200">
          <CircleCheckBig className="mt-0.5 size-4 shrink-0" />
          <p>Product description updated successfully.</p>
        </div>
      ) : null}

      {mutation.isError ? (
        <div className="mt-4 flex items-start gap-2 rounded-xl border border-destructive/25 bg-destructive/8 px-3 py-2 text-sm text-destructive">
          <CircleAlert className="mt-0.5 size-4 shrink-0" />
          <p>Failed to generate product description. Check the tenant AI profile settings and validation state.</p>
        </div>
      ) : null}

      {defaultProfile && defaultProfileIsUsable ? (
        <div className="mt-4 rounded-xl border border-border/70 bg-muted/20 p-3">
          <div className="flex items-center gap-2 text-sm">
            <Bot className="size-4 text-primary" />
            <span className="font-medium">{defaultProfile.name}</span>
            <Badge variant="outline">{defaultProfile.providerType}</Badge>
            <Badge variant="outline">{defaultProfile.model}</Badge>
          </div>
        </div>
      ) : null}
    </section>
  )
}
