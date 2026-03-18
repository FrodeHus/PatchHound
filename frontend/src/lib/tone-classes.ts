/**
 * Theme-aware tone classes for badges, pills, metric cards, and status indicators.
 * Uses CSS custom properties defined per-theme in app.css so colors adapt automatically.
 */

export type Tone = 'danger' | 'warning' | 'success' | 'info' | 'neutral'

/** Badge / pill: colored background + border + text */
const badgeMap: Record<Tone, string> = {
  danger: 'border-tone-danger-border bg-tone-danger text-tone-danger-foreground',
  warning: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
  success: 'border-tone-success-border bg-tone-success text-tone-success-foreground',
  info: 'border-tone-info-border bg-tone-info text-tone-info-foreground',
  neutral: 'border-tone-neutral-border bg-tone-neutral text-tone-neutral-foreground',
}

/** Dot (timeline markers): solid foreground color */
const dotMap: Record<Tone, string> = {
  danger: 'bg-tone-danger-foreground',
  warning: 'bg-tone-warning-foreground',
  success: 'bg-tone-success-foreground',
  info: 'bg-tone-info-foreground',
  neutral: 'bg-muted-foreground',
}

/** Text-only: foreground color for values/numbers */
const textMap: Record<Tone, string> = {
  danger: 'text-tone-danger-foreground',
  warning: 'text-tone-warning-foreground',
  success: 'text-tone-success-foreground',
  info: 'text-tone-info-foreground',
  neutral: 'text-foreground',
}

/** Surface: subtle background + border for cards/panels */
const surfaceMap: Record<Tone, string> = {
  danger: 'border-tone-danger-border bg-tone-danger',
  warning: 'border-tone-warning-border bg-tone-warning',
  success: 'border-tone-success-border bg-tone-success',
  info: 'border-tone-info-border bg-tone-info',
  neutral: 'border-border/70 bg-background',
}

export function toneBadge(tone: Tone): string {
  return badgeMap[tone]
}

export function toneDot(tone: Tone): string {
  return dotMap[tone]
}

export function toneText(tone: Tone): string {
  return textMap[tone]
}

export function toneSurface(tone: Tone): string {
  return surfaceMap[tone]
}
