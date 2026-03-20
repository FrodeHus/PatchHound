import { useMemo, useState } from 'react'
import { Calculator, RotateCcw, ShieldCheck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { toneBadge, toneText, type Tone } from '@/lib/tone-classes'
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

function severityTone(score: number): Tone {
  if (score >= 9.0) return 'danger'
  if (score >= 7.0) return 'warning'
  if (score >= 4.0) return 'info'
  return 'success'
}

/** Maps a CVSS metric key + value to a tone reflecting its impact on the overall score. */
function metricTone(key: string, value: string): Tone {
  const highImpact: Record<string, string[]> = {
    AV: ['N'], AC: ['L'], PR: ['N'], UI: ['N'], S: ['C'],
    C: ['H'], I: ['H'], A: ['H'],
    MAV: ['N'], MAC: ['L'], MPR: ['N'], MUI: ['N'], MS: ['C'],
    MC: ['H'], MI: ['H'], MA: ['H'],
    CR: ['H'], IR: ['H'], AR: ['H'],
  }
  const lowImpact: Record<string, string[]> = {
    AV: ['P', 'L'], AC: ['H'], PR: ['H'], UI: ['R'], S: ['U'],
    C: ['N'], I: ['N'], A: ['N'],
    MAV: ['P', 'L'], MAC: ['H'], MPR: ['H'], MUI: ['R'], MS: ['U'],
    MC: ['N'], MI: ['N'], MA: ['N'],
    CR: ['L'], IR: ['L'], AR: ['L'],
  }
  if (highImpact[key]?.includes(value)) return 'danger'
  if (lowImpact[key]?.includes(value)) return 'success'
  return 'warning'
}

export function CvssWorkbenchTrigger({
  vector,
  baseScore,
  securityProfile,
  title = 'CVSS 3.1 workbench',
  description = 'Inspect the vendor vector and explore alternate metric combinations.',
  triggerLabel = 'Open workbench',
}: CvssWorkbenchTriggerProps) {
  const metrics = useMemo(() => parseCvssVector(vector ?? null) ?? defaultMetrics, [vector])
  const vendorScore = baseScore ?? calculateCvssBaseScore(metrics)
  const severity = cvssSeverity(vendorScore)
  const tone = severityTone(vendorScore)
  const vendorPresentation = buildMetricPresentation(metrics)
  const environmental = securityProfile
    ? calculateCvssEnvironmentalScore(metrics, securityProfile)
    : null

  return (
    <Dialog>
      <InsetPanel className="space-y-3 px-4 py-4">
        <div className="flex items-center justify-between gap-4">
          <div className="flex items-center gap-4">
            <ScoreRing score={vendorScore} tone={tone} />
            {environmental ? (
              <>
                <span className="text-muted-foreground">&rarr;</span>
                <ScoreRing score={environmental.score} tone={severityTone(environmental.score)} />
              </>
            ) : null}
            <div>
              {environmental ? (
                <>
                  <p className="text-sm font-medium">{environmental.severity}</p>
                  <p className="mt-0.5 text-[11px] text-muted-foreground">
                    {vendorScore.toFixed(1)} vendor &rarr; {environmental.score.toFixed(1)} environmental
                  </p>
                </>
              ) : (
                <>
                  <p className="text-sm font-medium">{severity}</p>
                  <p className="mt-0.5 font-mono text-xs text-muted-foreground">
                    {vector ?? buildCvssVector(metrics)}
                  </p>
                </>
              )}
            </div>
          </div>
          <DialogTrigger
            render={<Button type="button" variant="outline" className="h-9 rounded-lg" />}
          >
            <Calculator className="size-3.5" />
            {triggerLabel}
          </DialogTrigger>
        </div>

        <div className="flex flex-wrap gap-1.5">
          {vendorPresentation.map((metric) => (
            <MetricPill key={metric.key} metric={metric} />
          ))}
        </div>
      </InsetPanel>

      <DialogContent className="w-[min(96vw,72rem)] max-w-[72rem] overflow-hidden rounded-2xl border-border/80 bg-card p-0 sm:max-w-[72rem]">
        <DialogHeader className="border-b border-border/60 px-5 py-4">
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <div className="max-h-[78vh] overflow-y-auto p-5">
          <CvssCalculator
            vector={vector}
            baseScore={baseScore}
            securityProfile={securityProfile}
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
  interactive = true,
}: CvssCalculatorProps) {
  const vendorMetrics = useMemo(() => parseCvssVector(vector ?? null) ?? defaultMetrics, [vector])
  const [metrics, setMetrics] = useState<CvssBaseMetrics>(vendorMetrics)

  const vendorScore = baseScore ?? calculateCvssBaseScore(vendorMetrics)
  const vendorSeverity = cvssSeverity(vendorScore)
  const vendorTone = severityTone(vendorScore)
  const vendorVector = vector ?? buildCvssVector(vendorMetrics)
  const vendorMetricPresentation = buildMetricPresentation(vendorMetrics)
  const environmental = securityProfile
    ? calculateCvssEnvironmentalScore(vendorMetrics, securityProfile)
    : null

  const calculatedScore = calculateCvssBaseScore(metrics)
  const calculatedSeverity = cvssSeverity(calculatedScore)
  const calculatedTone = severityTone(calculatedScore)
  const isModified = buildCvssVector(metrics) !== vendorVector

  return (
    <div className="space-y-4">
      {/* Score header */}
      <div className="flex flex-wrap items-start gap-6">
        <div className="flex items-center gap-4">
          <ScoreRing score={vendorScore} tone={vendorTone} size="lg" />
          <div>
            <p className="text-lg font-semibold">{vendorSeverity}</p>
            <p className="text-sm text-muted-foreground">Vendor base score</p>
          </div>
        </div>
        {environmental ? (
          <>
            <div className="hidden items-center self-center text-muted-foreground sm:flex">
              <span className="text-lg">&rarr;</span>
            </div>
            <div className="flex items-center gap-4">
              <ScoreRing score={environmental.score} tone={severityTone(environmental.score)} size="lg" />
              <div>
                <p className="text-lg font-semibold">{environmental.severity}</p>
                <p className="text-sm text-muted-foreground">
                  Environmental score
                  {securityProfile?.name ? ` (${securityProfile.name})` : ''}
                </p>
              </div>
            </div>
          </>
        ) : null}
      </div>

      {/* Vendor vector breakdown */}
      <InsetPanel className="space-y-3 px-4 py-4">
        <div className="flex items-center justify-between">
          <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">Vendor vector</p>
          <code className="rounded bg-muted px-2 py-0.5 font-mono text-xs text-muted-foreground">
            {vendorVector}
          </code>
        </div>
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
          {vendorMetricPresentation.map((metric) => (
            <MetricCard key={metric.key} metric={metric} />
          ))}
        </div>
      </InsetPanel>

      {/* Environmental modifiers (only when profile is applied) */}
      {environmental ? (
        <InsetPanel className="space-y-3 px-4 py-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <ShieldCheck className="size-3.5 text-primary" />
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">Environmental modifiers</p>
            </div>
            <code className="rounded bg-muted px-2 py-0.5 font-mono text-xs text-muted-foreground">
              {environmental.vector}
            </code>
          </div>
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
            {buildEnvironmentalPresentation(environmental).map((metric) => (
              <MetricCard key={metric.key} metric={metric} />
            ))}
          </div>
        </InsetPanel>
      ) : (
        <InsetPanel className="px-4 py-4">
          <p className="text-sm text-muted-foreground">
            No security profile is applied. Environmental scores are calculated per-asset based on each asset's assigned security profile.
          </p>
        </InsetPanel>
      )}

      {/* Security profile details */}
      {securityProfile ? (
        <InsetPanel className="space-y-3 px-4 py-4">
          <div className="flex items-center gap-2">
            <ShieldCheck className="size-3.5 text-primary" />
            <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
              Security profile: {securityProfile.name ?? 'Unnamed'}
            </p>
          </div>
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
            <ProfileField label="Reachability" value={securityProfile.internetReachability} />
            <ProfileField label="Confidentiality req." value={securityProfile.confidentialityRequirement} />
            <ProfileField label="Integrity req." value={securityProfile.integrityRequirement} />
            <ProfileField label="Availability req." value={securityProfile.availabilityRequirement} />
          </div>
        </InsetPanel>
      ) : null}

      {/* Manual calculator */}
      {interactive ? (
        <InsetPanel className="space-y-4 px-4 py-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Calculator className="size-3.5 text-primary" />
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                Manual calculator
              </p>
            </div>
            {isModified ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-7 gap-1.5 text-xs"
                onClick={() => setMetrics(vendorMetrics)}
              >
                <RotateCcw className="size-3" />
                Reset to vendor
              </Button>
            ) : null}
          </div>

          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
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

          <div className="flex items-center gap-6 rounded-lg border border-border/60 bg-background/50 px-4 py-3">
            <div className="flex items-center gap-3">
              <ScoreRing score={calculatedScore} tone={calculatedTone} />
              <div>
                <p className="text-sm font-medium">{calculatedSeverity}</p>
                <p className="font-mono text-xs text-muted-foreground">{buildCvssVector(metrics)}</p>
              </div>
            </div>
            {isModified ? (
              <p className="text-xs text-muted-foreground">
                {calculatedScore > vendorScore
                  ? `+${(calculatedScore - vendorScore).toFixed(1)} from vendor`
                  : calculatedScore < vendorScore
                    ? `${(calculatedScore - vendorScore).toFixed(1)} from vendor`
                    : 'Same as vendor'}
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">Matches vendor score</p>
            )}
          </div>
        </InsetPanel>
      ) : null}
    </div>
  )
}

function ScoreRing({ score, tone, size = 'sm' }: { score: number; tone: Tone; size?: 'sm' | 'lg' }) {
  const dim = size === 'lg' ? 'size-14' : 'size-10'
  const textSize = size === 'lg' ? 'text-lg' : 'text-sm'
  return (
    <div className={`${dim} flex shrink-0 items-center justify-center rounded-full border-2 border-current ${toneText(tone)}`}>
      <span className={`${textSize} font-bold`}>{score.toFixed(1)}</span>
    </div>
  )
}

function MetricCard({ metric }: { metric: CvssMetricPresentation }) {
  const tone = metricTone(metric.key, metric.value)
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <div className="flex items-center gap-2.5 rounded-lg border border-border/60 bg-background/50 px-3 py-2" />
        }
      >
        <span className={`inline-flex h-5 min-w-5 items-center justify-center rounded font-mono text-[10px] font-bold ${toneBadge(tone)}`}>
          {metric.value}
        </span>
        <div className="min-w-0">
          <p className="truncate text-xs font-medium">{metric.shortLabel}</p>
          <p className="truncate text-[11px] text-muted-foreground">{metric.valueLabel}</p>
        </div>
      </TooltipTrigger>
      <TooltipContent className="max-w-xs">
        <p className="font-medium">{metric.shortLabel}: {metric.valueLabel}</p>
        <p className="mt-1 text-muted-foreground">{metric.description}</p>
      </TooltipContent>
    </Tooltip>
  )
}

function MetricPill({ metric }: { metric: CvssMetricPresentation }) {
  const tone = metricTone(metric.key, metric.value)
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span className={`inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[11px] font-medium ${toneBadge(tone)}`} />
        }
      >
        <span className="font-mono font-bold opacity-70">{metric.key}</span>
        <span>{metric.valueLabel}</span>
      </TooltipTrigger>
      <TooltipContent className="max-w-xs">
        <p className="font-medium">{metric.shortLabel}</p>
        <p className="mt-1 text-muted-foreground">{metric.description}</p>
      </TooltipContent>
    </Tooltip>
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
    <div className="grid gap-1.5">
      <label className="text-xs font-medium text-muted-foreground">{label}</label>
      <Select value={value} onValueChange={onValueChange}>
        <SelectTrigger className="h-9 rounded-lg border-border/80 bg-background/80 text-sm">
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

function ProfileField({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/60 bg-background/50 px-3 py-2">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-0.5 text-sm font-medium">{value}</p>
    </div>
  )
}
