import { describe, expect, it } from 'vitest'
import {
  RISK_SCORE_RANGES,
  riskDetectionScoreTone,
  riskGaugeBand,
  riskScoreBand,
  riskScoreTone,
} from './risk-scoring'

describe('risk scoring helpers', () => {
  it('maps v2 score bands at the documented thresholds', () => {
    expect(riskScoreBand(499)).toBe('low')
    expect(riskScoreBand(500)).toBe('medium')
    expect(riskScoreBand(699)).toBe('medium')
    expect(riskScoreBand(700)).toBe('high')
    expect(riskScoreBand(849)).toBe('high')
    expect(riskScoreBand(850)).toBe('critical')
  })

  it('exposes UI gauge bands and visible ranges for v2 thresholds', () => {
    expect(riskGaugeBand(500)).toBe('elevated')
    expect(riskGaugeBand(700)).toBe('high')
    expect(riskGaugeBand(850)).toBe('critical')
    expect(RISK_SCORE_RANGES.high).toBe('700-849')
    expect(RISK_SCORE_RANGES.critical).toBe('850-1000')
  })

  it('prefers overall v2 risk when toning detection scores', () => {
    expect(riskDetectionScoreTone(99, 510)).toBe('info')
    expect(riskDetectionScoreTone(95)).toBe('danger')
    expect(riskDetectionScoreTone(80)).toBe('warning')
    expect(riskScoreTone(850)).toBe('danger')
  })
})
