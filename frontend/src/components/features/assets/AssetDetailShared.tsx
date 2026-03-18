import type { ReactNode } from 'react'
import { formatUnknownValue, looksLikeOpaqueId, startCase } from '@/lib/formatting'
import type { MetadataRecord } from '@/components/features/assets/AssetDetailHelpers'
import { toneBadge } from '@/lib/tone-classes'

export function SectionHeader({
  eyebrow,
  title,
  description,
}: {
  eyebrow: string
  title: string
  description: string
}) {
  return (
    <div className="mb-4 space-y-1">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{eyebrow}</p>
      <h3 className="text-lg font-semibold">{title}</h3>
      <p className="text-sm text-muted-foreground">{description}</p>
    </div>
  )
}

export function DataCard({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string | number
  mono?: boolean
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background px-3 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className={mono ? 'mt-1 font-mono text-sm' : 'mt-1 text-sm font-medium'}>{value}</p>
    </div>
  )
}

export function Badge({
  children,
  tone,
}: {
  children: ReactNode
  tone: 'slate' | 'blue' | 'amber'
}) {
  const toneClass =
    tone === 'amber'
      ? toneBadge('warning')
      : tone === 'blue'
        ? toneBadge('info')
        : toneBadge('neutral')

  return (
    <span className={`rounded-full border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em] ${toneClass}`}>
      {children}
    </span>
  )
}

export function SkeletonBlock({ className }: { className: string }) {
  return <div className={`animate-pulse rounded-2xl bg-muted/50 ${className}`} />
}

export function KeyValueGrid({ metadata }: { metadata: MetadataRecord }) {
  return (
    <div className="grid gap-3 md:grid-cols-2">
      {Object.entries(metadata).map(([key, value]) => (
        <DataCard key={key} label={startCase(key)} value={formatUnknownValue(value)} mono={typeof value === 'string' && looksLikeOpaqueId(value)} />
      ))}
    </div>
  )
}
