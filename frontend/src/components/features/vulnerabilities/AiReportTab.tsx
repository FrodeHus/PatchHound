import { Link } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Bot, CircleAlert, CircleCheckBig, Sparkles } from 'lucide-react'
import { fetchTenantAiProfiles } from '@/api/ai-settings.functions'
import { generateAiReport } from '@/api/vulnerabilities.functions'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { formatDateTime } from '@/lib/formatting'

type AiReportTabProps = {
  vulnerabilityId: string
}

export function AiReportTab({ vulnerabilityId }: AiReportTabProps) {
  const { selectedTenantId } = useTenantScope()
  const profilesQuery = useQuery({
    queryKey: ['tenant-ai-profiles', selectedTenantId],
    queryFn: () => fetchTenantAiProfiles(),
    enabled: !!selectedTenantId,
  })
  const defaultProfile = (profilesQuery.data ?? []).find((profile) => profile.isDefault && profile.isEnabled) ?? null
  const defaultProfileIsUsable = defaultProfile?.lastValidationStatus === 'Valid'

  const mutation = useMutation({
    mutationFn: () =>
      generateAiReport({
        data: {
          id: vulnerabilityId,
        },
      }),
  })

  const canGenerate = !profilesQuery.isLoading && !!defaultProfile && defaultProfileIsUsable

  return (
    <section className="space-y-4 rounded-[22px] border border-border bg-card p-4">
      <header className="space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <Bot className="size-4 text-primary" />
              <h2 className="text-sm font-medium">Tenant AI report</h2>
            </div>
            <p className="text-sm text-muted-foreground">
              Generates a markdown report using this tenant&apos;s default AI profile.
            </p>
          </div>

          <Button
            type="button"
            disabled={mutation.isPending || !canGenerate}
            onClick={() => {
              mutation.mutate()
            }}
          >
            {mutation.isPending ? 'Generating...' : 'Generate AI report'}
          </Button>
        </div>

        {profilesQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Loading tenant AI configuration...</p>
        ) : defaultProfile ? (
          <div className="rounded-[18px] border border-border/70 bg-muted/20 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <Badge>Default profile</Badge>
              <Badge variant="outline">{defaultProfile.providerType}</Badge>
              <Badge variant="outline">{defaultProfile.model}</Badge>
              {defaultProfile.lastValidationStatus === 'Valid' ? (
                <Badge variant="secondary">Validated</Badge>
              ) : null}
            </div>
            <p className="mt-2 text-sm font-medium">{defaultProfile.name}</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {defaultProfile.lastValidatedAt
                ? `Last checked ${formatDateTime(defaultProfile.lastValidatedAt)}`
                : 'Not validated yet.'}
            </p>
            {defaultProfile.lastValidationStatus === 'Invalid' ? (
              <div className="mt-3 flex items-start gap-2 rounded-[16px] border border-destructive/25 bg-destructive/8 px-3 py-2 text-sm text-destructive">
                <CircleAlert className="mt-0.5 size-4 shrink-0" />
                <p>{defaultProfile.lastValidationError || 'The last validation attempt failed.'}</p>
              </div>
            ) : null}
            {defaultProfile.lastValidationStatus !== 'Valid' ? (
              <div className="mt-3 flex items-start gap-2 rounded-[16px] border border-amber-300/25 bg-amber-500/8 px-3 py-2 text-sm text-amber-900 dark:text-amber-200">
                <CircleAlert className="mt-0.5 size-4 shrink-0" />
                <p>AI report generation is disabled until the default profile validates successfully.</p>
              </div>
            ) : null}
          </div>
        ) : (
          <div className="rounded-[18px] border border-dashed border-border bg-muted/20 p-4">
            <div className="flex items-start gap-3">
              <CircleAlert className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
              <div className="space-y-3">
                <div className="space-y-1">
                  <p className="text-sm font-medium">No default AI profile is configured for this tenant.</p>
                  <p className="text-sm text-muted-foreground">
                    Configure and validate a tenant AI profile before generating reports.
                  </p>
                </div>
                <Link
                  to="/settings/ai"
                  className="inline-flex h-9 items-center rounded-lg border border-border bg-background px-3 text-sm font-medium transition-colors hover:bg-muted"
                >
                  Open AI settings
                </Link>
              </div>
            </div>
          </div>
        )}
      </header>

      {mutation.isSuccess ? (
        <article className="rounded-[18px] border border-border bg-muted/25 p-4">
          <div className="flex flex-wrap items-center gap-2">
            <CircleCheckBig className="size-4 text-primary" />
            <p className="text-sm font-medium">Report generated</p>
          </div>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <Badge>{mutation.data.profileName}</Badge>
            <Badge variant="outline">{mutation.data.providerType}</Badge>
            <Badge variant="outline">{mutation.data.model}</Badge>
            <Badge variant="outline">{formatDateTime(mutation.data.generatedAt)}</Badge>
          </div>
          <Separator className="my-4" />
          <div className="flex items-center gap-2 text-xs uppercase tracking-[0.16em] text-muted-foreground">
            <Sparkles className="size-3.5" />
            Generated markdown
          </div>
          <pre className="mt-3 whitespace-pre-wrap text-sm leading-6">{mutation.data.content}</pre>
        </article>
      ) : null}

      {mutation.isError ? (
        <div className="flex items-start gap-2 rounded-[18px] border border-destructive/25 bg-destructive/8 px-4 py-3 text-sm text-destructive">
          <CircleAlert className="mt-0.5 size-4 shrink-0" />
          <p>Failed to generate the AI report. Check the tenant AI profile settings and validation state.</p>
        </div>
      ) : null}
    </section>
  )
}
