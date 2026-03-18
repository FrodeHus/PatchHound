import { useEffect } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bot, CircleAlert, CircleCheckBig, FileText } from 'lucide-react'
import { fetchTenantAiProfiles } from '@/api/ai-settings.functions'
import {
  fetchTenantSoftwareDescriptionStatus,
  generateTenantSoftwareDescription,
} from '@/api/software.functions'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import { softwareQueryKeys } from '@/features/software/list-state'
import { formatDateTime } from '@/lib/formatting'
import { toneSurface } from '@/lib/tone-classes'

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
  const queryClient = useQueryClient()

  const profilesQuery = useQuery({
    queryKey: ['tenant-ai-profiles', selectedTenantId],
    queryFn: () => fetchTenantAiProfiles(),
    enabled: !!selectedTenantId,
  })
  const defaultProfile =
    (profilesQuery.data ?? []).find((profile) => profile.isDefault && profile.isEnabled) ?? null
  const defaultProfileIsUsable = defaultProfile?.lastValidationStatus === 'Valid'

  const statusQuery = useQuery({
    queryKey: ['tenant-software-description-job', selectedTenantId, tenantSoftwareId],
    queryFn: () => fetchTenantSoftwareDescriptionStatus({ data: { id: tenantSoftwareId } }),
    enabled: !!selectedTenantId,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status === 'Pending' || status === 'Running' ? 3000 : false
    },
  })

  useEffect(() => {
    if (statusQuery.data?.status === 'Succeeded') {
      void queryClient.invalidateQueries({
        queryKey: softwareQueryKeys.detail(selectedTenantId, tenantSoftwareId),
      })
    }
  }, [queryClient, selectedTenantId, statusQuery.data?.status, tenantSoftwareId])

  const mutation = useMutation({
    mutationFn: () =>
      generateTenantSoftwareDescription({
        data: {
          id: tenantSoftwareId,
        },
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ['tenant-software-description-job', selectedTenantId, tenantSoftwareId],
      })
    },
  })

  const latestJob = statusQuery.data
  const descriptionJobIsActive =
    latestJob?.status === 'Pending' || latestJob?.status === 'Running'
  const canGenerate =
    !profilesQuery.isLoading && !!defaultProfile && defaultProfileIsUsable && !descriptionJobIsActive

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
          {mutation.isPending || descriptionJobIsActive
            ? latestJob?.status === 'Running'
              ? 'Generating...'
              : 'Queued...'
            : initialDescription
              ? 'Regenerate'
              : 'Generate description'}
        </Button>
      </div>

      {initialDescription ? (
        <div className="mt-4 space-y-3">
          {initialGeneratedAt && initialProviderType && initialProfileName && initialModel ? (
            <div className="flex flex-wrap items-center gap-2">
              <Badge>{initialProfileName}</Badge>
              <Badge variant="outline">{initialProviderType}</Badge>
              <Badge variant="outline">{initialModel}</Badge>
              <Badge variant="outline">{formatDateTime(initialGeneratedAt)}</Badge>
            </div>
          ) : null}
          <MarkdownViewer content={initialDescription} />
        </div>
      ) : (
        <div className="mt-4 rounded-xl border border-dashed border-border bg-muted/20 p-4">
          <p className="text-sm text-muted-foreground">
            No product description has been generated yet.
          </p>
        </div>
      )}

      {defaultProfile && !defaultProfileIsUsable ? (
        <div className={`mt-4 flex items-start gap-2 rounded-xl ${toneSurface('warning')} px-3 py-2 text-sm text-tone-warning-foreground`}>
          <CircleAlert className="mt-0.5 size-4 shrink-0" />
          <p>Product description generation is disabled until the default AI profile validates successfully.</p>
        </div>
      ) : null}

      {descriptionJobIsActive ? (
        <div className={`mt-4 flex items-start gap-2 rounded-xl ${toneSurface('info')} px-3 py-2 text-sm text-tone-info-foreground`}>
          <Bot className="mt-0.5 size-4 shrink-0" />
          <p>
            Product description generation is queued in the background. This page will refresh when the job completes.
          </p>
        </div>
      ) : null}

      {mutation.isSuccess ? (
        <div className={`mt-4 flex items-start gap-2 rounded-xl ${toneSurface('success')} px-3 py-2 text-sm text-tone-success-foreground`}>
          <CircleCheckBig className="mt-0.5 size-4 shrink-0" />
          <p>Product description job queued successfully.</p>
        </div>
      ) : null}

      {mutation.isError || latestJob?.status === 'Failed' ? (
        <div className="mt-4 flex items-start gap-2 rounded-xl border border-destructive/25 bg-destructive/8 px-3 py-2 text-sm text-destructive">
          <CircleAlert className="mt-0.5 size-4 shrink-0" />
          <p>
            {latestJob?.status === 'Failed'
              ? latestJob.error || 'Product description generation failed.'
              : 'Failed to queue product description generation. Check the tenant AI profile settings and validation state.'}
          </p>
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
