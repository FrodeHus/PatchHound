import { useMemo, useState } from 'react'
import { Calculator, ShieldCheck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import {
  buildCvssVector,
  buildEnvironmentalPresentation,
  buildMetricPresentation,
  calculateCvssBaseScore,
  calculateCvssEnvironmentalScore,
  cvssMetricDefinitions,
  cvssSeverity,
  parseCvssVector,
  type CvssBaseMetrics,
  type CvssMetricPresentation,
  type CvssSecurityProfileAdjustment,
} from '@/lib/cvss'

type CvssCalculatorProps = {
  vector?: string | null
  baseScore?: number | null
  securityProfile?: CvssSecurityProfileAdjustment | null
  title?: string
  description?: string
  interactive?: boolean
}

type CvssWorkbenchTriggerProps = {
  vector?: string | null
  baseScore?: number | null
  securityProfile?: CvssSecurityProfileAdjustment | null
  title?: string
  description?: string
  triggerLabel?: string
}

const defaultMetrics: CvssBaseMetrics = {
  attackVector: 'N',
  attackComplexity: 'L',
  privilegesRequired: 'N',
  userInteraction: 'N',
  scope: 'U',
  confidentiality: 'H',
  integrity: 'H',
  availability: 'H',
}

export function CvssWorkbenchTrigger({
  vector,
  baseScore,
  securityProfile,
  title = 'CVSS 3.1 scoring',
  description = 'Inspect the vendor vector and, when a security profile applies, the environmental score used to better reflect organizational exposure.',
  triggerLabel = 'Inspect calculation',
}: CvssWorkbenchTriggerProps) {
  const metrics = useMemo(() => parseCvssVector(vector ?? null) ?? defaultMetrics, [vector])
  const vendorScore = baseScore ?? calculateCvssBaseScore(metrics)
  const environmental = securityProfile ? calculateCvssEnvironmentalScore(metrics, securityProfile) : null
  const delta = environmental ? environmental.score - vendorScore : 0

  return (
    <Dialog>
      <InsetPanel className="space-y-4 px-4 py-4">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">CVSS Preview</p>
            <p className="text-sm text-muted-foreground">
              Review the vendor score, environmental score, and open the full workbench only when needed.
            </p>
          </div>
          <DialogTrigger
            render={<Button type="button" variant="outline" />}
          >
            <Calculator className="size-4" />
            {triggerLabel}
          </DialogTrigger>
        </div>

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <ScoreCard label="Vendor score" value={vendorScore.toFixed(1)} detail={cvssSeverity(vendorScore)} />
          <ScoreCard
            label="Environmental score"
            value={(environmental ? environmental.score : vendorScore).toFixed(1)}
            detail={environmental ? environmental.severity : 'Matches vendor score'}
          />
          <ScoreCard
            label="Delta"
            value={`${delta >= 0 ? '+' : ''}${delta.toFixed(1)}`}
            detail={delta === 0 ? 'No change' : delta > 0 ? 'Higher than vendor' : 'Lower than vendor'}
          />
          <ScoreCard
            label="Vector"
            value={environmental?.vector ?? vector ?? buildCvssVector(metrics)}
            detail={securityProfile?.name ?? 'Vendor base vector'}
            mono
          />
        </div>
      </InsetPanel>

      <DialogContent className="w-[min(96vw,78rem)] max-w-[78rem] overflow-hidden rounded-2xl border-border/80 bg-card p-0 sm:max-w-[78rem]">
        <DialogHeader className="border-b border-border/60 px-5 py-4">
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <div className="max-h-[78vh] overflow-y-auto p-5">
          <CvssCalculator
            vector={vector}
            baseScore={baseScore}
            securityProfile={securityProfile}
            title="CVSS workbench"
            description="Use the manual calculator to inspect the vendor vector and environmental result without cluttering the main workflow."
          />
        </div>
      </DialogContent>
    </Dialog>
  )
}

export function CvssCalculator({
  vector,
  baseScore,
  securityProfile,
  title = 'CVSS 3.1 scoring',
  description = 'Review the vendor vector and, when a security profile applies, the environmental score used to better reflect organizational exposure.',
  interactive = true,
}: CvssCalculatorProps) {
  const initialMetrics = useMemo(() => parseCvssVector(vector ?? null) ?? defaultMetrics, [vector])
  const [metrics, setMetrics] = useState<CvssBaseMetrics>(initialMetrics)

  const effectiveMetrics = vector ? parseCvssVector(vector) ?? metrics : metrics
  const calculatedBaseScore = calculateCvssBaseScore(effectiveMetrics)
  const vendorScore = baseScore ?? calculatedBaseScore
  const vendorSeverity = cvssSeverity(vendorScore)
  const computedVector = buildCvssVector(effectiveMetrics)
  const vendorMetricPresentation = buildMetricPresentation(effectiveMetrics)
  const environmental = securityProfile
    ? calculateCvssEnvironmentalScore(effectiveMetrics, securityProfile)
    : null

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="space-y-2">
        <div className="flex items-center gap-2">
          <Calculator className="size-4 text-primary" />
          <h3 className="text-lg font-semibold">{title}</h3>
        </div>
        <p className="max-w-3xl text-sm leading-6 text-muted-foreground">{description}</p>
      </div>

      <div className="mt-4 grid gap-3 lg:grid-cols-2 xl:grid-cols-4">
        <ScoreCard label="Vendor score" value={vendorScore.toFixed(1)} detail={vendorSeverity} />
        <ScoreCard label="Vendor vector" value={vector ?? computedVector} detail="CVSS 3.1 base vector" mono />
        <ScoreCard
          label="Environmental score"
          value={environmental ? environmental.score.toFixed(1) : vendorScore.toFixed(1)}
          detail={environmental ? environmental.severity : 'Matches vendor score'}
        />
        <ScoreCard
          label="Environmental vector"
          value={environmental?.vector ?? 'No profile modifiers'}
          detail={securityProfile?.name ?? (securityProfile ? 'Profile modifiers applied' : 'No security profile')}
          mono
        />
      </div>

      {interactive ? (
        <InsetPanel className="mt-4 space-y-4 px-4 py-4">
          <div className="flex items-center gap-2 text-sm font-medium">
            <Calculator className="size-4 text-primary" />
            Manual calculator
          </div>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <MetricSelect
              label="Attack Vector"
              metricKey="AV"
              value={metrics.attackVector}
              onValueChange={(value) => setMetrics((current) => ({ ...current, attackVector: value as CvssBaseMetrics['attackVector'] }))}
            />
            <MetricSelect
              label="Attack Complexity"
              metricKey="AC"
              value={metrics.attackComplexity}
              onValueChange={(value) => setMetrics((current) => ({ ...current, attackComplexity: value as CvssBaseMetrics['attackComplexity'] }))}
            />
            <MetricSelect
              label="Privileges Required"
              metricKey="PR"
              value={metrics.privilegesRequired}
              onValueChange={(value) => setMetrics((current) => ({ ...current, privilegesRequired: value as CvssBaseMetrics['privilegesRequired'] }))}
            />
            <MetricSelect
              label="User Interaction"
              metricKey="UI"
              value={metrics.userInteraction}
              onValueChange={(value) => setMetrics((current) => ({ ...current, userInteraction: value as CvssBaseMetrics['userInteraction'] }))}
            />
            <MetricSelect
              label="Scope"
              metricKey="S"
              value={metrics.scope}
              onValueChange={(value) => setMetrics((current) => ({ ...current, scope: value as CvssBaseMetrics['scope'] }))}
            />
            <MetricSelect
              label="Confidentiality"
              metricKey="C"
              value={metrics.confidentiality}
              onValueChange={(value) => setMetrics((current) => ({ ...current, confidentiality: value as CvssBaseMetrics['confidentiality'] }))}
            />
            <MetricSelect
              label="Integrity"
              metricKey="I"
              value={metrics.integrity}
              onValueChange={(value) => setMetrics((current) => ({ ...current, integrity: value as CvssBaseMetrics['integrity'] }))}
            />
            <MetricSelect
              label="Availability"
              metricKey="A"
              value={metrics.availability}
              onValueChange={(value) => setMetrics((current) => ({ ...current, availability: value as CvssBaseMetrics['availability'] }))}
            />
          </div>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <ScoreCard label="Calculated base score" value={calculateCvssBaseScore(metrics).toFixed(1)} detail={cvssSeverity(calculateCvssBaseScore(metrics))} />
            <ScoreCard label="Generated vector" value={buildCvssVector(metrics)} detail="Manual calculator output" mono />
          </div>
        </InsetPanel>
      ) : null}

      <div className="mt-4 grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <MetricStrip title="Vendor vector breakdown" items={vendorMetricPresentation} />
        <MetricStrip
          title={securityProfile ? 'Environmental modifiers' : 'Environmental modifiers'}
          items={environmental ? buildEnvironmentalPresentation(environmental) : []}
          emptyMessage="No security profile is applied, so the environmental score matches the vendor CVSS score."
        />
      </div>

      {securityProfile ? (
        <InsetPanel className="mt-4 px-4 py-4">
          <div className="flex items-center gap-2 text-sm font-medium">
            <ShieldCheck className="size-4 text-primary" />
            Security profile adjustment
          </div>
          <p className="mt-2 text-sm text-muted-foreground">
            The security profile provides authoritative CVSS Environmental modified metrics. Requirement levels still adjust the CR, IR, and AR multipliers.
          </p>
          <div className="mt-3 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <ProfileAdjustment label="Reachability" value={securityProfile.internetReachability} />
            <ProfileAdjustment label="Modified AV" value={securityProfile.modifiedAttackVector} />
            <ProfileAdjustment label="Modified AC" value={securityProfile.modifiedAttackComplexity} />
            <ProfileAdjustment label="Modified PR" value={securityProfile.modifiedPrivilegesRequired} />
            <ProfileAdjustment label="Modified UI" value={securityProfile.modifiedUserInteraction} />
            <ProfileAdjustment label="Modified Scope" value={securityProfile.modifiedScope} />
            <ProfileAdjustment label="Modified C" value={securityProfile.modifiedConfidentialityImpact} />
            <ProfileAdjustment label="Modified I" value={securityProfile.modifiedIntegrityImpact} />
            <ProfileAdjustment label="Modified A" value={securityProfile.modifiedAvailabilityImpact} />
            <ProfileAdjustment label="Confidentiality requirement" value={securityProfile.confidentialityRequirement} />
            <ProfileAdjustment label="Integrity requirement" value={securityProfile.integrityRequirement} />
            <ProfileAdjustment label="Availability requirement" value={securityProfile.availabilityRequirement} />
          </div>
        </InsetPanel>
      ) : null}
    </section>
  )
}

function MetricSelect({
  label,
  metricKey,
  value,
  onValueChange,
}: {
  label: string
  metricKey: keyof typeof cvssMetricDefinitions
  value: string
  onValueChange: (value: string | null) => void
}) {
  const definition = cvssMetricDefinitions[metricKey]
  const options = Object.entries(definition.values) as Array<
    [string, { label: string; description: string }]
  >

  return (
    <div className="grid gap-2">
      <label className="text-sm font-medium">{label}</label>
      <Select value={value} onValueChange={onValueChange}>
        <SelectTrigger className="h-11 rounded-lg border-border/90 bg-[color-mix(in_oklab,var(--background)_82%,black)]">
          <SelectValue />
        </SelectTrigger>
        <SelectContent className="rounded-xl border-border/80 bg-popover/98 backdrop-blur">
          {options.map(([optionValue, option]) => (
            <SelectItem key={optionValue} value={optionValue}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}

function ScoreCard({
  label,
  value,
  detail,
  mono = false,
}: {
  label: string
  value: string
  detail: string
  mono?: boolean
}) {
  return (
    <InsetPanel emphasis="strong" className="px-4 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className={mono ? 'mt-2 break-all font-mono text-sm font-medium' : 'mt-2 text-2xl font-semibold'}>{value}</p>
      <p className="mt-1 text-xs text-muted-foreground">{detail}</p>
    </InsetPanel>
  )
}

function MetricStrip({
  title,
  items,
  emptyMessage = 'No metrics available.',
}: {
  title: string
  items: CvssMetricPresentation[]
  emptyMessage?: string
}) {
  return (
    <InsetPanel className="space-y-3 px-4 py-4">
      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{title}</p>
      {items.length ? (
        <div className="flex flex-wrap gap-2">
          {items.map((metric) => (
            <Tooltip key={`${metric.key}:${metric.value}`}>
              <TooltipTrigger
                render={
                  <span className="inline-flex items-center gap-2 rounded-full border border-sky-300/50 bg-sky-50 px-3 py-1.5 text-xs font-medium text-sky-950" />
                }
              >
                <span className="text-[11px] uppercase tracking-[0.18em] text-sky-700">{metric.key}</span>
                <span>{metric.valueLabel}</span>
              </TooltipTrigger>
              <TooltipContent className="max-w-sm">
                <div className="space-y-1">
                  <p className="font-medium">{metric.shortLabel}</p>
                  <p>{metric.description}</p>
                </div>
              </TooltipContent>
            </Tooltip>
          ))}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">{emptyMessage}</p>
      )}
    </InsetPanel>
  )
}

function ProfileAdjustment({ label, value }: { label: string; value: string }) {
  return (
    <InsetPanel emphasis="strong" className="px-3 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium">{value}</p>
    </InsetPanel>
  )
}
