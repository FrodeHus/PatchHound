/**
 * Target-aware score posture.
 *
 * - score ≤ target          → Stable   (success)
 * - score ≤ target + target/3  → Elevated (warning / concerning)
 * - score > target + target/3  → Critical (danger / alarming)
 */

type ScorePosture = {
  label: string
  tone: 'success' | 'warning' | 'danger'
}

export function scorePosture(score: number, target: number): ScorePosture {
  if (score <= target) {
    return { label: 'Stable', tone: 'success' }
  }
  if (score <= target + target / 3) {
    return { label: 'Elevated', tone: 'warning' }
  }
  return { label: 'Critical', tone: 'danger' }
}

const toneMap = {
  success: {
    text: 'text-tone-success-foreground',
    bar: 'bg-tone-success-foreground/80',
    badge: 'border-tone-success-border bg-tone-success text-tone-success-foreground',
    icon: 'border-chart-3/20 bg-chart-3/10 text-chart-3',
  },
  warning: {
    text: 'text-tone-warning-foreground',
    bar: 'bg-tone-warning-foreground/80',
    badge: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
    icon: 'border-tone-warning-border bg-tone-warning/40 text-tone-warning-foreground',
  },
  danger: {
    text: 'text-tone-danger-foreground',
    bar: 'bg-tone-danger-foreground/80',
    badge: 'border-tone-danger-border bg-tone-danger text-tone-danger-foreground',
    icon: 'border-destructive/20 bg-destructive/10 text-destructive',
  },
} as const

export function postureText(tone: ScorePosture['tone']) {
  return toneMap[tone].text
}

export function postureBar(tone: ScorePosture['tone']) {
  return toneMap[tone].bar
}

export function postureBadge(tone: ScorePosture['tone']) {
  return toneMap[tone].badge
}

export function postureIcon(tone: ScorePosture['tone']) {
  return toneMap[tone].icon
}
