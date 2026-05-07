import { useMemo, type ReactNode } from 'react'
import { AlertTriangle, ArrowUp, Menu, ShieldAlert, Sigma } from 'lucide-react'
import type { SecurityProfile } from '@/api/security-profiles.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  calculateCvssBaseScore,
  calculateCvssEnvironmentalScore,
  cvssSeverity,
  parseCvssVector,
  type CvssSecurityProfileAdjustment,
} from '@/lib/cvss'
import type { SecurityProfileDraft } from './security-profile-workbench-model'
import {
  securityProfileEnvironmentClassOptions,
  securityProfileInternetReachabilityHelp,
  securityProfileInternetReachabilityOptions,
  securityProfileModifiedAttackComplexityOptions,
  securityProfileModifiedAttackVectorOptions,
  securityProfileModifiedImpactOptions,
  securityProfileModifiedPrivilegesRequiredOptions,
  securityProfileModifiedScopeOptions,
  securityProfileModifiedUserInteractionOptions,
  securityProfileRequirementHelp,
  securityProfileRequirementOptions,
} from '@/lib/options/security-profiles'

type SecurityProfileWorkbenchProps = {
  mode: 'create' | 'edit'
  tenantName: string
  profile?: SecurityProfile | null
  draft: SecurityProfileDraft
  isSaving?: boolean
  onDraftChange: (draft: SecurityProfileDraft) => void
  onCancel: () => void
  onSave: () => void
}

const previewVector = 'CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:L/A:N'


export function SecurityProfileWorkbench({
  mode,
  tenantName,
  profile,
  draft,
  isSaving = false,
  onDraftChange,
  onCancel,
  onSave,
}: SecurityProfileWorkbenchProps) {
  const canSave = draft.name.trim().length > 0
  const baseMetrics = useMemo(() => parseCvssVector(previewVector)!, [])
  const baseScore = useMemo(() => calculateCvssBaseScore(baseMetrics), [baseMetrics])
  const environmental = useMemo(
    () => calculateCvssEnvironmentalScore(baseMetrics, toAdjustment(draft)),
    [baseMetrics, draft],
  )
  const modifiedImpact = useMemo(
    () => calculateCvssEnvironmentalScore(baseMetrics, toModifiedImpactAdjustment(draft)),
    [baseMetrics, draft],
  )
  const requirementsOnly = useMemo(
    () => calculateCvssEnvironmentalScore(baseMetrics, toRequirementsOnlyAdjustment(draft)),
    [baseMetrics, draft],
  )
  const temporalScore = Math.max(0, baseScore - 0.3)
  const delta = environmental.score - baseScore
  const modifiedImpactDelta = modifiedImpact.score - temporalScore
  const requirementsDelta = environmental.score - modifiedImpact.score
  const overrideCount = countOverrides(draft)

  return (
    <section className="relative -m-4 min-h-[calc(100vh-4rem)] space-y-5 overflow-hidden bg-[linear-gradient(125deg,#8dbbff_0%,#ead3ff_44%,#ee9eff_68%,#58f1e8_100%)] px-4 pb-24 pt-4 text-slate-950 sm:-m-6 sm:px-6 sm:pt-5">
      <div className="relative overflow-hidden">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="max-w-3xl">
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-slate-600">
              Security profiles · CVSS environmental
            </p>
            <h1 className="mt-2 text-3xl font-semibold text-slate-950">
              {draft.name.trim() || (mode === 'edit' ? profile?.name : 'New environmental profile')}
            </h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-700">
              Apply environmental context on top of the base CVSS v3.1 score so the recalculated rating reflects how this asset group is actually deployed and which security goals matter most.
            </p>
          </div>
          {overrideCount > 0 ? (
            <Badge className="rounded-full border-orange-400/45 bg-orange-200/20 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.08em] text-orange-600">
              <AlertTriangle className="size-3" />
              Override active
            </Badge>
          ) : null}
        </div>

        <div className="mt-8 grid gap-4 rounded-2xl border border-white/55 bg-white/22 px-5 py-4 shadow-[0_18px_70px_rgb(44_33_103_/_0.16)] backdrop-blur-xl lg:grid-cols-4">
          <ScoreStat label="Base score" value={baseScore.toFixed(1)} badge={cvssSeverity(baseScore)} detail="CVSS v3.1 - Network / Low / None" />
          <ScoreStat label="Temporal" value={temporalScore.toFixed(1)} detail="Functional · Official Fix · Confirmed" />
          <ScoreStat label="Environmental" value={environmental.score.toFixed(1)} badge={environmental.severity} detail="Recalculated for this profile" />
          <ScoreStat
            label="Delta from base"
            value={`${delta >= 0 ? '+' : ''}${delta.toFixed(1)}`}
            detail={deltaReason(draft)}
            intent={delta > 0 ? 'danger' : delta < 0 ? 'success' : 'neutral'}
            leadingIcon={delta > 0 ? <ArrowUp className="size-5" /> : null}
          />
        </div>
      </div>

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
        <div className="space-y-5">
          <WorkbenchPanel number="1" title="Profile" description="Name and scope. Profiles can be assigned to one or many assets.">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Profile name">
                <Input
                  value={draft.name}
                  onChange={(event) => patchDraft(onDraftChange, draft, { name: event.target.value })}
                  placeholder="Production payments - Internet-facing"
                  className="h-10 border-white/45 bg-white/28"
                />
              </Field>
              <Field label="Environment class">
                <OptionSelect
                  value={draft.environmentClass}
                  options={securityProfileEnvironmentClassOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { environmentClass: value as SecurityProfileDraft['environmentClass'] })}
                />
              </Field>
              <Field label="Description">
                <Input
                  value={draft.description}
                  onChange={(event) => patchDraft(onDraftChange, draft, { description: event.target.value })}
                  placeholder="Systems reachable from the public internet."
                  className="h-10 border-white/45 bg-white/28"
                />
              </Field>
              <Field label="Applies to">
                <div className="flex h-10 items-center rounded-lg border border-white/45 bg-white/22 px-3 text-sm text-slate-700">
                  Tenant scope: {tenantName}
                </div>
              </Field>
            </div>
          </WorkbenchPanel>

          <WorkbenchPanel
            number="2"
            title="Security requirements"
            badge="CR / IR / AR"
            description="How much each security goal matters for this asset context."
          >
            <RequirementControl
              label="Confidentiality requirement"
              value={draft.confidentialityRequirement}
              onChange={(value) => patchDraft(onDraftChange, draft, { confidentialityRequirement: value })}
            />
            <RequirementControl
              label="Integrity requirement"
              value={draft.integrityRequirement}
              onChange={(value) => patchDraft(onDraftChange, draft, { integrityRequirement: value })}
            />
            <RequirementControl
              label="Availability requirement"
              value={draft.availabilityRequirement}
              onChange={(value) => patchDraft(onDraftChange, draft, { availabilityRequirement: value })}
            />
          </WorkbenchPanel>

          <WorkbenchPanel
            number="3"
            title="Modified base metrics"
            badge="M*"
            description="Override how the vulnerability behaves in this environment. Leave rows at Not defined to inherit vendor values."
          >
            <div className="grid gap-4 lg:grid-cols-2">
              <Field label="Internet reachability" helper={securityProfileInternetReachabilityHelp[draft.internetReachability]}>
                <OptionSelect
                  value={draft.internetReachability}
                  options={securityProfileInternetReachabilityOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { internetReachability: value as SecurityProfileDraft['internetReachability'] })}
                />
              </Field>
              <Field label="Modified attack vector">
                <OptionSelect
                  value={draft.modifiedAttackVector}
                  options={securityProfileModifiedAttackVectorOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedAttackVector: value as SecurityProfileDraft['modifiedAttackVector'] })}
                />
              </Field>
              <Field label="Modified attack complexity">
                <OptionSelect
                  value={draft.modifiedAttackComplexity}
                  options={securityProfileModifiedAttackComplexityOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedAttackComplexity: value as SecurityProfileDraft['modifiedAttackComplexity'] })}
                />
              </Field>
              <Field label="Modified privileges required">
                <OptionSelect
                  value={draft.modifiedPrivilegesRequired}
                  options={securityProfileModifiedPrivilegesRequiredOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedPrivilegesRequired: value as SecurityProfileDraft['modifiedPrivilegesRequired'] })}
                />
              </Field>
              <Field label="Modified user interaction">
                <OptionSelect
                  value={draft.modifiedUserInteraction}
                  options={securityProfileModifiedUserInteractionOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedUserInteraction: value as SecurityProfileDraft['modifiedUserInteraction'] })}
                />
              </Field>
              <Field label="Modified scope">
                <OptionSelect
                  value={draft.modifiedScope}
                  options={securityProfileModifiedScopeOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedScope: value as SecurityProfileDraft['modifiedScope'] })}
                />
              </Field>
            </div>
          </WorkbenchPanel>

          <WorkbenchPanel number="4" title="Modified impact metrics" description="Set explicit environmental impact only when the local deployment changes vendor assumptions.">
            <div className="grid gap-4 md:grid-cols-3">
              <Field label="Confidentiality impact">
                <OptionSelect
                  value={draft.modifiedConfidentialityImpact}
                  options={securityProfileModifiedImpactOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedConfidentialityImpact: value as SecurityProfileDraft['modifiedConfidentialityImpact'] })}
                />
              </Field>
              <Field label="Integrity impact">
                <OptionSelect
                  value={draft.modifiedIntegrityImpact}
                  options={securityProfileModifiedImpactOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedIntegrityImpact: value as SecurityProfileDraft['modifiedIntegrityImpact'] })}
                />
              </Field>
              <Field label="Availability impact">
                <OptionSelect
                  value={draft.modifiedAvailabilityImpact}
                  options={securityProfileModifiedImpactOptions}
                  onValueChange={(value) => patchDraft(onDraftChange, draft, { modifiedAvailabilityImpact: value as SecurityProfileDraft['modifiedAvailabilityImpact'] })}
                />
              </Field>
            </div>
          </WorkbenchPanel>
        </div>

        <aside className="space-y-5">
          <Card className="rounded-2xl border-white/45 bg-white/22 shadow-none backdrop-blur-xl">
            <CardContent className="p-5">
              <div className="flex items-center gap-2">
                <span className="flex size-6 items-center justify-center rounded-lg bg-white/55 text-xs">
                  <Sigma className="size-3.5" />
                </span>
                <h2 className="text-sm font-semibold">Recalculated score</h2>
              </div>
              <ScoreGauge score={environmental.score} />
              <p className="mt-2 text-center text-[10px] font-bold uppercase tracking-[0.24em] text-muted-foreground">
                {environmental.severity} · environmental
              </p>
            </CardContent>
          </Card>

          <SummaryPanel title="Breakdown" icon={<Menu className="size-3.5" />}>
            <BreakdownRow label="Base" value={baseScore.toFixed(1)} />
            <BreakdownRow label="Temporal" value={temporalScore.toFixed(1)} delta={temporalScore - baseScore} />
            <BreakdownRow label="Mod. impact (C/I/A)" value={modifiedImpact.score.toFixed(1)} delta={modifiedImpactDelta} />
            <BreakdownRow label="Security reqs (CR/IR/AR)" value={requirementsOnly.score.toFixed(1)} delta={requirementsDelta} />
            <BreakdownRow label="Compensating controls" value={environmental.score.toFixed(1)} delta={0} />
          </SummaryPanel>

          <SummaryPanel title="Effect on findings" icon={<ShieldAlert className="size-3.5" />}>
            <BreakdownRow label="Severity" value={environmental.severity} />
            <BreakdownRow label="Override count" value={String(overrideCount)} />
            <BreakdownRow label="Reachability" value={readable(draft.internetReachability)} />
          </SummaryPanel>
        </aside>
      </div>

      <div className="fixed inset-x-0 bottom-0 z-40 border-t border-white/45 bg-white/25 px-5 py-3 backdrop-blur-xl">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3">
          <p className="text-xs text-slate-600">
            {mode === 'edit' ? 'Editing existing profile' : 'New draft profile'} · {tenantName}
          </p>
          <div className="flex items-center gap-3">
            <Button type="button" variant="outline" className="border-white/55 bg-white/45" onClick={onCancel}>Cancel</Button>
            <Button type="button" variant="outline" className="border-white/55 bg-white/45" disabled={!canSave || isSaving} onClick={onSave}>
              {isSaving ? 'Saving...' : 'Save as draft'}
            </Button>
            <Button type="button" disabled={!canSave || isSaving} onClick={onSave}>
              {isSaving ? 'Saving...' : 'Apply profile'}
            </Button>
          </div>
        </div>
      </div>
    </section>
  )
}

function WorkbenchPanel({
  number,
  title,
  badge,
  description,
  children,
}: {
  number: string
  title: string
  badge?: string
  description: string
  children: ReactNode
}) {
  return (
    <Card className="rounded-2xl border-white/45 bg-white/22 shadow-none backdrop-blur-xl">
      <CardContent className="space-y-4 p-5">
        <div className="flex items-start gap-3">
          <span className="flex size-6 shrink-0 items-center justify-center rounded-lg bg-white/60 text-xs font-semibold">{number}</span>
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="text-sm font-semibold">{title}</h2>
              {badge ? <Badge variant="secondary" className="rounded-md px-2 py-0 text-[10px]">{badge}</Badge> : null}
            </div>
            <p className="mt-1 text-xs leading-5 text-slate-600">{description}</p>
          </div>
        </div>
        {children}
      </CardContent>
    </Card>
  )
}

function ScoreStat({
  label,
  value,
  badge,
  detail,
  intent = 'neutral',
  leadingIcon,
}: {
  label: string
  value: string
  badge?: string
  detail: string
  intent?: 'neutral' | 'danger' | 'success'
  leadingIcon?: ReactNode
}) {
  const valueClass = intent === 'danger' ? 'text-rose-500' : intent === 'success' ? 'text-emerald-600' : 'text-slate-950'

  return (
    <div className="border-white/30 lg:border-r lg:pr-5 last:border-r-0">
      <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-600">{label}</p>
      <div className="mt-1 flex min-w-0 items-center gap-2">
        {leadingIcon}
        <p className={['truncate text-3xl font-semibold', valueClass].join(' ')}>{value}</p>
        {badge ? <Badge variant="outline" className="rounded-full border-white/45 bg-white/30 px-2 py-0 text-[10px] font-bold uppercase">{badge}</Badge> : null}
      </div>
      <p className="mt-1 text-xs text-slate-600">{detail}</p>
    </div>
  )
}

function RequirementControl({
  label,
  value,
  onChange,
}: {
  label: string
  value: SecurityProfileDraft['confidentialityRequirement']
  onChange: (value: SecurityProfileDraft['confidentialityRequirement']) => void
}) {
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs font-medium text-slate-600">{label}</p>
        <p className="text-[11px] text-slate-500">{securityProfileRequirementHelp[value]}</p>
      </div>
      <Segmented
        value={value}
        options={securityProfileRequirementOptions}
        labels={{ Low: 'Low', Medium: 'Medium', High: 'High' }}
        onChange={onChange}
      />
    </div>
  )
}

function Segmented<T extends string>({
  value,
  options,
  labels,
  onChange,
}: {
  value: T
  options: readonly T[]
  labels?: Partial<Record<T, string>>
  onChange: (value: T) => void
}) {
  return (
    <div className="grid rounded-xl border border-white/45 bg-white/20 p-1" style={{ gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` }}>
      {options.map((option) => (
        <button
          key={option}
          type="button"
          onClick={() => onChange(option)}
          className={[
            'h-9 cursor-pointer rounded-lg px-2 text-xs font-medium transition-colors',
            value === option ? 'bg-white/65 text-slate-950 shadow-sm' : 'text-slate-600 hover:bg-white/30',
          ].join(' ')}
        >
          {labels?.[option] ?? readable(option)}
        </button>
      ))}
    </div>
  )
}

function Field({ label, helper, children }: { label: string; helper?: string; children: ReactNode }) {
  return (
    <label className="grid gap-2">
      <span className="text-xs font-semibold text-slate-600">{label}</span>
      {children}
      {helper ? <span className="text-[11px] leading-4 text-slate-500">{helper}</span> : null}
    </label>
  )
}

function OptionSelect({
  value,
  options,
  onValueChange,
}: {
  value: string
  options: readonly string[]
  onValueChange: (value: string) => void
}) {
  return (
    <Select value={value} onValueChange={(next) => next && onValueChange(next)}>
      <SelectTrigger className="h-10 w-full border-white/45 bg-white/28">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {options.map((option) => (
          <SelectItem key={option} value={option}>
            {readable(option)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

function SummaryPanel({ title, icon, children }: { title: string; icon: ReactNode; children: ReactNode }) {
  return (
    <Card className="rounded-2xl border-white/45 bg-white/22 shadow-none backdrop-blur-xl">
      <CardContent className="p-5">
        <div className="mb-4 flex items-center gap-2">
          <span className="flex size-6 items-center justify-center rounded-lg bg-white/55 text-xs">{icon}</span>
          <h2 className="text-sm font-semibold">{title}</h2>
        </div>
        <div className="space-y-3">{children}</div>
      </CardContent>
    </Card>
  )
}

function BreakdownRow({ label, value, delta, mono = false }: { label: string; value: string; delta?: number; mono?: boolean }) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-slate-900/10 pb-2 last:border-0 last:pb-0">
      <span className="text-xs text-slate-600">{label}</span>
      <span className={['max-w-[13rem] text-right text-xs font-medium text-slate-950', mono ? 'font-mono leading-5' : ''].join(' ')}>
        {value}
        {typeof delta === 'number' ? (
          <span className={delta > 0 ? 'ml-2 rounded bg-rose-200/40 px-1.5 py-0.5 text-rose-500' : delta < 0 ? 'ml-2 rounded bg-emerald-200/40 px-1.5 py-0.5 text-emerald-600' : 'ml-2 rounded bg-slate-200/45 px-1.5 py-0.5 text-slate-500'}>
            {delta >= 0 ? '+' : ''}{delta.toFixed(1)}
          </span>
        ) : null}
      </span>
    </div>
  )
}

function ScoreGauge({ score }: { score: number }) {
  const degrees = Math.max(0, Math.min(1, score / 10)) * 180

  return (
    <div className="mx-auto mt-4 grid aspect-[2/1] w-full max-w-[260px] place-items-end overflow-hidden">
      <div
        className="relative aspect-[2/1] w-full rounded-t-full"
        style={{ background: `conic-gradient(from 270deg at 50% 100%, #67d27c 0deg, #e8d240 84deg, #ef6262 180deg, transparent 180deg)` }}
      >
        <div className="absolute inset-x-[11%] bottom-0 aspect-[2/1] rounded-t-full bg-white/70" />
        <div
          className="absolute bottom-0 left-1/2 h-[42%] w-1 origin-bottom rounded-full bg-foreground"
          style={{ transform: `translateX(-50%) rotate(${degrees - 90}deg)` }}
        />
        <div className="absolute inset-x-0 bottom-2 text-center">
          <p className="text-5xl font-semibold">{score.toFixed(1)}</p>
        </div>
      </div>
    </div>
  )
}

function patchDraft(
  onDraftChange: (draft: SecurityProfileDraft) => void,
  draft: SecurityProfileDraft,
  patch: Partial<SecurityProfileDraft>,
) {
  onDraftChange({ ...draft, ...patch })
}

function toAdjustment(draft: SecurityProfileDraft): CvssSecurityProfileAdjustment {
  return {
    internetReachability: draft.internetReachability,
    confidentialityRequirement: draft.confidentialityRequirement,
    integrityRequirement: draft.integrityRequirement,
    availabilityRequirement: draft.availabilityRequirement,
    modifiedAttackVector: draft.modifiedAttackVector,
    modifiedAttackComplexity: draft.modifiedAttackComplexity,
    modifiedPrivilegesRequired: draft.modifiedPrivilegesRequired,
    modifiedUserInteraction: draft.modifiedUserInteraction,
    modifiedScope: draft.modifiedScope,
    modifiedConfidentialityImpact: draft.modifiedConfidentialityImpact,
    modifiedIntegrityImpact: draft.modifiedIntegrityImpact,
    modifiedAvailabilityImpact: draft.modifiedAvailabilityImpact,
  }
}

function toModifiedImpactAdjustment(draft: SecurityProfileDraft): CvssSecurityProfileAdjustment {
  return {
    ...toAdjustment(draft),
    confidentialityRequirement: 'Medium',
    integrityRequirement: 'Medium',
    availabilityRequirement: 'Medium',
  }
}

function toRequirementsOnlyAdjustment(draft: SecurityProfileDraft): CvssSecurityProfileAdjustment {
  return {
    ...toAdjustment(draft),
    modifiedAttackVector: 'NotDefined',
    modifiedAttackComplexity: 'NotDefined',
    modifiedPrivilegesRequired: 'NotDefined',
    modifiedUserInteraction: 'NotDefined',
    modifiedScope: 'NotDefined',
    modifiedConfidentialityImpact: 'NotDefined',
    modifiedIntegrityImpact: 'NotDefined',
    modifiedAvailabilityImpact: 'NotDefined',
  }
}

function countOverrides(draft: SecurityProfileDraft) {
  return [
    draft.modifiedAttackVector,
    draft.modifiedAttackComplexity,
    draft.modifiedPrivilegesRequired,
    draft.modifiedUserInteraction,
    draft.modifiedScope,
    draft.modifiedConfidentialityImpact,
    draft.modifiedIntegrityImpact,
    draft.modifiedAvailabilityImpact,
  ].filter((value) => value !== 'NotDefined').length
}

function deltaReason(draft: SecurityProfileDraft) {
  const raised = [
    draft.confidentialityRequirement === 'High' ? 'confidentiality' : null,
    draft.integrityRequirement === 'High' ? 'integrity' : null,
    draft.availabilityRequirement === 'High' ? 'availability' : null,
  ].filter(Boolean)

  return raised.length > 0 ? `${raised.join(' & ')} raised to High` : 'No high requirement selected'
}

function readable(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^Not Defined$/i, 'Not defined')
}
