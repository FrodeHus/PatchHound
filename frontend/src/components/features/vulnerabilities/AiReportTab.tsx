import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { generateAiReport } from '@/api/vulnerabilities.functions'

type AiReportTabProps = {
  vulnerabilityId: string
}

export function AiReportTab({ vulnerabilityId }: AiReportTabProps) {
  const [providerName, setProviderName] = useState('mock')
  const mutation = useMutation({
    mutationFn: (provider: string) =>
      generateAiReport({
        data: {
          id: vulnerabilityId,
          providerName: provider,
        },
      }),
  })

  return (
    <section className="space-y-3 rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-center gap-2">
        <input
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={providerName}
          onChange={(event) => {
            setProviderName(event.target.value)
          }}
          placeholder="Provider name"
        />
        <button
          type="button"
          className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
          disabled={mutation.isPending || providerName.trim().length === 0}
          onClick={() => {
            mutation.mutate(providerName)
          }}
        >
          {mutation.isPending ? 'Generating...' : 'Generate AI report'}
        </button>
      </div>

      {mutation.isSuccess ? (
        <article className="rounded-md border border-border bg-muted/30 p-3">
          <p className="mb-2 text-xs text-muted-foreground">
            Provider: {mutation.data.provider} | {new Date(mutation.data.generatedAt).toLocaleString()}
          </p>
          <pre className="whitespace-pre-wrap text-sm">{mutation.data.content}</pre>
        </article>
      ) : null}

      {mutation.isError ? <p className="text-sm text-destructive">Failed to generate AI report.</p> : null}
    </section>
  )
}
