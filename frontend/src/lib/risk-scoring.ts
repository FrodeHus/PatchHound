import type { Tone } from '@/lib/tone-classes'

export type RiskScoreBand = 'low' | 'medium' | 'high' | 'critical'
export type RiskGaugeBand = 'contained' | 'elevated' | 'high' | 'critical'

export const RISK_SCORE_THRESHOLDS = {
  medium: 500,
  high: 700,
  critical: 850,
} as const

export const RISK_SCORE_RANGES = {
  low: '0-499',
  medium: '500-699',
  high: '700-849',
  critical: '850-1000',
} as const

export function riskScoreBand(score: number): RiskScoreBand {
  if (score >= RISK_SCORE_THRESHOLDS.critical) return 'critical'
  if (score >= RISK_SCORE_THRESHOLDS.high) return 'high'
  if (score >= RISK_SCORE_THRESHOLDS.medium) return 'medium'
  return 'low'
}

export function riskGaugeBand(score: number): RiskGaugeBand {
  const band = riskScoreBand(score)
  if (band === 'low') return 'contained'
  if (band === 'medium') return 'elevated'
  return band
}

export function riskScoreTone(score: number): Tone {
  const band = riskScoreBand(score)
  if (band === 'critical') return 'danger'
  if (band === 'high') return 'warning'
  if (band === 'medium') return 'info'
  return 'success'
}

export function riskScoreBadgeClass(score: number): string {
  const band = riskScoreBand(score)
  if (band === 'critical') return 'border-destructive/25 bg-destructive/10 text-destructive'
  if (band === 'high') return 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground'
  if (band === 'medium') return 'border-chart-2/25 bg-chart-2/10 text-chart-2'
  return 'border-border/70 bg-background/40 text-muted-foreground'
}

export function riskDetectionScoreTone(detectionScore: number, overallRiskScore?: number | null): Tone {
  if (typeof overallRiskScore === 'number') {
    return riskScoreTone(overallRiskScore)
  }

  if (detectionScore >= 95) return 'danger'
  if (detectionScore >= 80) return 'warning'
  return 'neutral'
}
