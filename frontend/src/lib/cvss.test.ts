import { describe, expect, it } from 'vitest'
import {
  buildCvssVector,
  calculateCvssBaseScore,
  calculateCvssEnvironmentalScore,
  parseCvssVector,
} from '@/lib/cvss'

describe('cvss', () => {
  it('parses a CVSS 3.1 base vector and reproduces the expected base score', () => {
    const metrics = parseCvssVector('CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H')

    expect(metrics).not.toBeNull()
    expect(metrics?.attackVector).toBe('N')
    expect(buildCvssVector(metrics!)).toBe('CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H')
    expect(calculateCvssBaseScore(metrics!)).toBe(9.8)
  })

  it('applies security profile adjustments to produce an environmental score', () => {
    const metrics = parseCvssVector('CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H')
    const result = calculateCvssEnvironmentalScore(metrics!, {
      internetReachability: 'LocalOnly',
      confidentialityRequirement: 'High',
      integrityRequirement: 'Medium',
      availabilityRequirement: 'Low',
      modifiedAttackVector: 'Local',
      modifiedAttackComplexity: 'NotDefined',
      modifiedPrivilegesRequired: 'NotDefined',
      modifiedUserInteraction: 'NotDefined',
      modifiedScope: 'NotDefined',
      modifiedConfidentialityImpact: 'NotDefined',
      modifiedIntegrityImpact: 'NotDefined',
      modifiedAvailabilityImpact: 'NotDefined',
    })

    expect(result.modifiedAttackVector).toBe('L')
    expect(result.confidentialityRequirement).toBe('H')
    expect(result.availabilityRequirement).toBe('L')
    expect(result.score).toBeLessThan(9.8)
    expect(result.vector).toBe('CVSS:3.1/MAV:L/MAC:L/MPR:N/MUI:N/MS:U/MC:H/MI:H/MA:H/CR:H/IR:M/AR:L')
  })

  it('uses explicit modified metrics as the environmental authority', () => {
    const metrics = parseCvssVector('CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H')
    const result = calculateCvssEnvironmentalScore(metrics!, {
      internetReachability: 'Internet',
      confidentialityRequirement: 'Medium',
      integrityRequirement: 'Medium',
      availabilityRequirement: 'Medium',
      modifiedAttackVector: 'Local',
      modifiedAttackComplexity: 'High',
      modifiedPrivilegesRequired: 'High',
      modifiedUserInteraction: 'Required',
      modifiedScope: 'Unchanged',
      modifiedConfidentialityImpact: 'Low',
      modifiedIntegrityImpact: 'Low',
      modifiedAvailabilityImpact: 'Low',
    })

    expect(result.modifiedAttackVector).toBe('L')
    expect(result.modifiedAttackComplexity).toBe('H')
    expect(result.modifiedPrivilegesRequired).toBe('H')
    expect(result.modifiedUserInteraction).toBe('R')
    expect(result.modifiedConfidentialityImpact).toBe('L')
    expect(result.score).toBeLessThan(9.8)
  })
})
