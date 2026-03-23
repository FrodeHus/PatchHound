import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { ArrowLeft } from 'lucide-react'
import type { SoftwareRemediationContext, SoftwareRemediationVuln } from '@/api/remediation.schemas'
import { RemediationSummaryCards } from './RemediationSummaryCards'
import { RemediationVulnTable } from './RemediationVulnTable'
import { RemediationVulnDrawer } from './RemediationVulnDrawer'

type SoftwareRemediationViewProps = {
  data: SoftwareRemediationContext
}

export function SoftwareRemediationView({ data }: SoftwareRemediationViewProps) {
  const [selectedVuln, setSelectedVuln] = useState<SoftwareRemediationVuln | null>(null)

  return (
    <section className="space-y-5">
      <header className="flex flex-wrap items-center gap-3">
        <Link
          to="/assets/$id"
          params={{ id: data.assetId }}
          className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" />
          Back to asset
        </Link>
        <div className="flex items-center gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{data.assetName}</h1>
          <span className="rounded-full border border-border/70 bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
            {data.criticality} criticality
          </span>
        </div>
      </header>

      <RemediationSummaryCards summary={data.summary} />

      <div>
        <h2 className="mb-3 text-lg font-semibold">Vulnerabilities</h2>
        <RemediationVulnTable
          vulnerabilities={data.vulnerabilities}
          onSelectVuln={setSelectedVuln}
        />
      </div>

      <RemediationVulnDrawer
        vuln={selectedVuln}
        isOpen={selectedVuln !== null}
        onOpenChange={(open) => { if (!open) setSelectedVuln(null) }}
      />
    </section>
  )
}
