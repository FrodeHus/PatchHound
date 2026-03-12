export type CvssAttackVector = 'N' | 'A' | 'L' | 'P'
export type CvssAttackComplexity = 'L' | 'H'
export type CvssPrivilegesRequired = 'N' | 'L' | 'H'
export type CvssUserInteraction = 'N' | 'R'
export type CvssScope = 'U' | 'C'
export type CvssImpact = 'N' | 'L' | 'H'
export type CvssRequirement = 'L' | 'M' | 'H'
export type CvssModifiedAttackVector = 'NotDefined' | 'Network' | 'Adjacent' | 'Local' | 'Physical'
export type CvssModifiedAttackComplexity = 'NotDefined' | 'Low' | 'High'
export type CvssModifiedPrivilegesRequired = 'NotDefined' | 'None' | 'Low' | 'High'
export type CvssModifiedUserInteraction = 'NotDefined' | 'None' | 'Required'
export type CvssModifiedScope = 'NotDefined' | 'Unchanged' | 'Changed'
export type CvssModifiedImpact = 'NotDefined' | 'None' | 'Low' | 'High'

export type CvssBaseMetrics = {
  attackVector: CvssAttackVector
  attackComplexity: CvssAttackComplexity
  privilegesRequired: CvssPrivilegesRequired
  userInteraction: CvssUserInteraction
  scope: CvssScope
  confidentiality: CvssImpact
  integrity: CvssImpact
  availability: CvssImpact
}

export type CvssSecurityProfileAdjustment = {
  name?: string | null
  internetReachability: 'Internet' | 'InternalNetwork' | 'AdjacentOnly' | 'LocalOnly'
  confidentialityRequirement: 'Low' | 'Medium' | 'High'
  integrityRequirement: 'Low' | 'Medium' | 'High'
  availabilityRequirement: 'Low' | 'Medium' | 'High'
  modifiedAttackVector: CvssModifiedAttackVector
  modifiedAttackComplexity: CvssModifiedAttackComplexity
  modifiedPrivilegesRequired: CvssModifiedPrivilegesRequired
  modifiedUserInteraction: CvssModifiedUserInteraction
  modifiedScope: CvssModifiedScope
  modifiedConfidentialityImpact: CvssModifiedImpact
  modifiedIntegrityImpact: CvssModifiedImpact
  modifiedAvailabilityImpact: CvssModifiedImpact
}

export type CvssMetricPresentation = {
  key: string
  shortLabel: string
  value: string
  valueLabel: string
  description: string
}

export type CvssEnvironmentalResult = {
  modifiedAttackVector: CvssAttackVector
  modifiedAttackComplexity: CvssAttackComplexity
  modifiedPrivilegesRequired: CvssPrivilegesRequired
  modifiedUserInteraction: CvssUserInteraction
  modifiedScope: CvssScope
  modifiedConfidentialityImpact: CvssImpact
  modifiedIntegrityImpact: CvssImpact
  modifiedAvailabilityImpact: CvssImpact
  confidentialityRequirement: CvssRequirement
  integrityRequirement: CvssRequirement
  availabilityRequirement: CvssRequirement
  score: number
  severity: string
  vector: string
}

type CvssRequirementLevel = CvssSecurityProfileAdjustment['confidentialityRequirement']

export const cvssMetricDefinitions = {
  AV: {
    label: 'Attack Vector',
    values: {
      N: { label: 'Network', description: 'Exploitable remotely over the network.' },
      A: { label: 'Adjacent', description: 'Requires access to the same shared network or broadcast domain.' },
      L: { label: 'Local', description: 'Requires local or logged-in access to the target.' },
      P: { label: 'Physical', description: 'Requires physical interaction with the target device.' },
    },
  },
  AC: {
    label: 'Attack Complexity',
    values: {
      L: { label: 'Low', description: 'No special conditions are required beyond the vulnerable state.' },
      H: { label: 'High', description: 'Successful exploitation depends on uncommon conditions or preparation.' },
    },
  },
  PR: {
    label: 'Privileges Required',
    values: {
      N: { label: 'None', description: 'No privileges are required before exploitation.' },
      L: { label: 'Low', description: 'Basic user-level privileges are required.' },
      H: { label: 'High', description: 'Administrative or otherwise elevated privileges are required.' },
    },
  },
  UI: {
    label: 'User Interaction',
    values: {
      N: { label: 'None', description: 'No action from another user is required.' },
      R: { label: 'Required', description: 'Another user must perform some action for exploitation to succeed.' },
    },
  },
  S: {
    label: 'Scope',
    values: {
      U: { label: 'Unchanged', description: 'The exploited component and impacted component share the same security scope.' },
      C: { label: 'Changed', description: 'Exploitation can impact resources beyond the vulnerable component’s scope.' },
    },
  },
  C: {
    label: 'Confidentiality',
    values: {
      N: { label: 'None', description: 'No confidentiality impact is expected.' },
      L: { label: 'Low', description: 'Limited disclosure of information is possible.' },
      H: { label: 'High', description: 'Serious or total disclosure of sensitive information is possible.' },
    },
  },
  I: {
    label: 'Integrity',
    values: {
      N: { label: 'None', description: 'No integrity impact is expected.' },
      L: { label: 'Low', description: 'Limited modification of data is possible.' },
      H: { label: 'High', description: 'Serious or total compromise of data integrity is possible.' },
    },
  },
  A: {
    label: 'Availability',
    values: {
      N: { label: 'None', description: 'No availability impact is expected.' },
      L: { label: 'Low', description: 'Performance degradation or interruptions are limited.' },
      H: { label: 'High', description: 'Serious service disruption or total loss of availability is possible.' },
    },
  },
  CR: {
    label: 'Confidentiality Requirement',
    values: {
      L: { label: 'Low', description: 'Confidentiality matters less for this environment.' },
      M: { label: 'Medium', description: 'Balanced default requirement.' },
      H: { label: 'High', description: 'Confidentiality matters more for this environment.' },
    },
  },
  IR: {
    label: 'Integrity Requirement',
    values: {
      L: { label: 'Low', description: 'Integrity matters less for this environment.' },
      M: { label: 'Medium', description: 'Balanced default requirement.' },
      H: { label: 'High', description: 'Integrity matters more for this environment.' },
    },
  },
  AR: {
    label: 'Availability Requirement',
    values: {
      L: { label: 'Low', description: 'Availability matters less for this environment.' },
      M: { label: 'Medium', description: 'Balanced default requirement.' },
      H: { label: 'High', description: 'Availability matters more for this environment.' },
    },
  },
  MAV: {
    label: 'Modified Attack Vector',
    values: {
      N: { label: 'Network', description: 'Profile still treats the vulnerability as network-reachable.' },
      A: { label: 'Adjacent', description: 'Profile limits exploitation to adjacent network conditions.' },
      L: { label: 'Local', description: 'Profile limits exploitation to local access conditions.' },
      P: { label: 'Physical', description: 'Profile limits exploitation to physical access conditions.' },
    },
  },
  MAC: {
    label: 'Modified Attack Complexity',
    values: {
      L: { label: 'Low', description: 'The environment does not add unusual exploit preconditions.' },
      H: { label: 'High', description: 'The environment adds conditions that make exploitation harder.' },
    },
  },
  MPR: {
    label: 'Modified Privileges Required',
    values: {
      N: { label: 'None', description: 'No prior privileges are needed in this environment.' },
      L: { label: 'Low', description: 'Basic privileges are needed in this environment.' },
      H: { label: 'High', description: 'Elevated privileges are needed in this environment.' },
    },
  },
  MUI: {
    label: 'Modified User Interaction',
    values: {
      N: { label: 'None', description: 'No other user action is needed in this environment.' },
      R: { label: 'Required', description: 'Another user action is still required in this environment.' },
    },
  },
  MS: {
    label: 'Modified Scope',
    values: {
      U: { label: 'Unchanged', description: 'Impact stays in the same security scope.' },
      C: { label: 'Changed', description: 'Impact crosses into another security scope.' },
    },
  },
  MC: {
    label: 'Modified Confidentiality Impact',
    values: {
      N: { label: 'None', description: 'No confidentiality impact in this environment.' },
      L: { label: 'Low', description: 'Limited confidentiality impact in this environment.' },
      H: { label: 'High', description: 'High confidentiality impact in this environment.' },
    },
  },
  MI: {
    label: 'Modified Integrity Impact',
    values: {
      N: { label: 'None', description: 'No integrity impact in this environment.' },
      L: { label: 'Low', description: 'Limited integrity impact in this environment.' },
      H: { label: 'High', description: 'High integrity impact in this environment.' },
    },
  },
  MA: {
    label: 'Modified Availability Impact',
    values: {
      N: { label: 'None', description: 'No availability impact in this environment.' },
      L: { label: 'Low', description: 'Limited availability impact in this environment.' },
      H: { label: 'High', description: 'High availability impact in this environment.' },
    },
  },
} as const

export function parseCvssVector(vector: string | null): CvssBaseMetrics | null {
  if (!vector) {
    return null
  }

  const parts = vector.split('/').slice(1)
  const map = new Map(parts.map((segment) => segment.split(':', 2) as [string, string]))

  const attackVector = map.get('AV')
  const attackComplexity = map.get('AC')
  const privilegesRequired = map.get('PR')
  const userInteraction = map.get('UI')
  const scope = map.get('S')
  const confidentiality = map.get('C')
  const integrity = map.get('I')
  const availability = map.get('A')

  if (
    !isAttackVector(attackVector)
    || !isAttackComplexity(attackComplexity)
    || !isPrivilegesRequired(privilegesRequired)
    || !isUserInteraction(userInteraction)
    || !isScope(scope)
    || !isImpact(confidentiality)
    || !isImpact(integrity)
    || !isImpact(availability)
  ) {
    return null
  }

  return {
    attackVector,
    attackComplexity,
    privilegesRequired,
    userInteraction,
    scope,
    confidentiality,
    integrity,
    availability,
  }
}

export function buildCvssVector(metrics: CvssBaseMetrics) {
  return `CVSS:3.1/AV:${metrics.attackVector}/AC:${metrics.attackComplexity}/PR:${metrics.privilegesRequired}/UI:${metrics.userInteraction}/S:${metrics.scope}/C:${metrics.confidentiality}/I:${metrics.integrity}/A:${metrics.availability}`
}

export function calculateCvssBaseScore(metrics: CvssBaseMetrics) {
  const scopeChanged = metrics.scope === 'C'
  const impactSubScore = 1
    - (1 - impactWeight(metrics.confidentiality))
      * (1 - impactWeight(metrics.integrity))
      * (1 - impactWeight(metrics.availability))

  const impact = scopeChanged
    ? 7.52 * (impactSubScore - 0.029) - 3.25 * Math.pow(impactSubScore - 0.02, 15)
    : 6.42 * impactSubScore

  const exploitability =
    8.22
    * attackVectorWeight(metrics.attackVector)
    * attackComplexityWeight(metrics.attackComplexity)
    * privilegesRequiredWeight(metrics.privilegesRequired, scopeChanged)
    * userInteractionWeight(metrics.userInteraction)

  if (impact <= 0) {
    return 0
  }

  const score = scopeChanged
    ? Math.min(1.08 * (impact + exploitability), 10)
    : Math.min(impact + exploitability, 10)

  return roundUp1(score)
}

export function calculateCvssEnvironmentalScore(
  metrics: CvssBaseMetrics,
  profile: CvssSecurityProfileAdjustment,
): CvssEnvironmentalResult {
  const modifiedAttackVector = resolveModifiedAttackVector(metrics.attackVector, profile.modifiedAttackVector)
  const modifiedAttackComplexity = resolveModifiedAttackComplexity(metrics.attackComplexity, profile.modifiedAttackComplexity)
  const modifiedPrivilegesRequired = resolveModifiedPrivilegesRequired(metrics.privilegesRequired, profile.modifiedPrivilegesRequired)
  const modifiedUserInteraction = resolveModifiedUserInteraction(metrics.userInteraction, profile.modifiedUserInteraction)
  const modifiedScope = resolveModifiedScope(metrics.scope, profile.modifiedScope)
  const modifiedConfidentialityImpact = resolveModifiedImpact(metrics.confidentiality, profile.modifiedConfidentialityImpact)
  const modifiedIntegrityImpact = resolveModifiedImpact(metrics.integrity, profile.modifiedIntegrityImpact)
  const modifiedAvailabilityImpact = resolveModifiedImpact(metrics.availability, profile.modifiedAvailabilityImpact)
  const confidentialityRequirement = requirementCode(profile.confidentialityRequirement)
  const integrityRequirement = requirementCode(profile.integrityRequirement)
  const availabilityRequirement = requirementCode(profile.availabilityRequirement)

  const miss = Math.min(
    1
      - (1 - impactWeight(modifiedConfidentialityImpact) * requirementMultiplier(confidentialityRequirement))
        * (1 - impactWeight(modifiedIntegrityImpact) * requirementMultiplier(integrityRequirement))
        * (1 - impactWeight(modifiedAvailabilityImpact) * requirementMultiplier(availabilityRequirement)),
    0.915,
  )

  const scopeChanged = modifiedScope === 'C'
  const modifiedImpact = scopeChanged
    ? 7.52 * (miss - 0.029) - 3.25 * Math.pow(miss * 0.9731 - 0.02, 13)
    : 6.42 * miss

  const exploitability =
    8.22
    * attackVectorWeight(modifiedAttackVector)
    * attackComplexityWeight(modifiedAttackComplexity)
    * privilegesRequiredWeight(modifiedPrivilegesRequired, scopeChanged)
    * userInteractionWeight(modifiedUserInteraction)

  const score = modifiedImpact <= 0
    ? 0
    : scopeChanged
      ? Math.min(1.08 * (modifiedImpact + exploitability), 10)
      : Math.min(modifiedImpact + exploitability, 10)

  return {
    modifiedAttackVector,
    modifiedAttackComplexity,
    modifiedPrivilegesRequired,
    modifiedUserInteraction,
    modifiedScope,
    modifiedConfidentialityImpact,
    modifiedIntegrityImpact,
    modifiedAvailabilityImpact,
    confidentialityRequirement,
    integrityRequirement,
    availabilityRequirement,
    score: roundUp1(score),
    severity: cvssSeverity(roundUp1(score)),
    vector: [
      'CVSS:3.1',
      `MAV:${modifiedAttackVector}`,
      `MAC:${modifiedAttackComplexity}`,
      `MPR:${modifiedPrivilegesRequired}`,
      `MUI:${modifiedUserInteraction}`,
      `MS:${modifiedScope}`,
      `MC:${modifiedConfidentialityImpact}`,
      `MI:${modifiedIntegrityImpact}`,
      `MA:${modifiedAvailabilityImpact}`,
      `CR:${confidentialityRequirement}`,
      `IR:${integrityRequirement}`,
      `AR:${availabilityRequirement}`,
    ].join('/'),
  }
}

export function cvssSeverity(score: number | null | undefined) {
  if (score === null || score === undefined) {
    return 'Not scored'
  }

  if (score === 0) {
    return 'None'
  }

  if (score < 4) {
    return 'Low'
  }

  if (score < 7) {
    return 'Medium'
  }

  if (score < 9) {
    return 'High'
  }

  return 'Critical'
}

export function buildMetricPresentation(metrics: CvssBaseMetrics): CvssMetricPresentation[] {
  return [
    metricPresentation('AV', metrics.attackVector),
    metricPresentation('AC', metrics.attackComplexity),
    metricPresentation('PR', metrics.privilegesRequired),
    metricPresentation('UI', metrics.userInteraction),
    metricPresentation('S', metrics.scope),
    metricPresentation('C', metrics.confidentiality),
    metricPresentation('I', metrics.integrity),
    metricPresentation('A', metrics.availability),
  ]
}

export function buildEnvironmentalPresentation(result: CvssEnvironmentalResult): CvssMetricPresentation[] {
  return [
    metricPresentation('MAV', result.modifiedAttackVector),
    metricPresentation('MAC', result.modifiedAttackComplexity),
    metricPresentation('MPR', result.modifiedPrivilegesRequired),
    metricPresentation('MUI', result.modifiedUserInteraction),
    metricPresentation('MS', result.modifiedScope),
    metricPresentation('MC', result.modifiedConfidentialityImpact),
    metricPresentation('MI', result.modifiedIntegrityImpact),
    metricPresentation('MA', result.modifiedAvailabilityImpact),
    metricPresentation('CR', result.confidentialityRequirement),
    metricPresentation('IR', result.integrityRequirement),
    metricPresentation('AR', result.availabilityRequirement),
  ]
}

function metricPresentation(
  key: keyof typeof cvssMetricDefinitions,
  value: string,
): CvssMetricPresentation {
  const definition = cvssMetricDefinitions[key] as {
    label: string
    values: Record<string, { label: string; description: string }>
  }
  const valueDefinition = definition.values[value]

  return {
    key,
    shortLabel: definition.label,
    value: String(value),
    valueLabel: valueDefinition.label,
    description: valueDefinition.description,
  }
}

function impactWeight(value: CvssImpact) {
  return value === 'H' ? 0.56 : value === 'L' ? 0.22 : 0
}

function attackVectorWeight(value: CvssAttackVector) {
  return value === 'N' ? 0.85 : value === 'A' ? 0.62 : value === 'L' ? 0.55 : 0.2
}

function attackComplexityWeight(value: CvssAttackComplexity) {
  return value === 'H' ? 0.44 : 0.77
}

function privilegesRequiredWeight(value: CvssPrivilegesRequired, scopeChanged: boolean) {
  if (value === 'N') {
    return 0.85
  }

  if (value === 'L') {
    return scopeChanged ? 0.68 : 0.62
  }

  return scopeChanged ? 0.5 : 0.27
}

function userInteractionWeight(value: CvssUserInteraction) {
  return value === 'R' ? 0.62 : 0.85
}

function roundUp1(input: number) {
  return Math.ceil(input * 10) / 10
}

function requirementCode(value: CvssRequirementLevel): CvssRequirement {
  return value === 'Low' ? 'L' : value === 'High' ? 'H' : 'M'
}

function requirementMultiplier(value: CvssRequirement) {
  return value === 'L' ? 0.5 : value === 'H' ? 1.5 : 1
}

function resolveModifiedAttackVector(
  baseAttackVector: CvssAttackVector,
  modifiedAttackVector: CvssModifiedAttackVector,
): CvssAttackVector {
  return modifiedAttackVector === 'Network'
    ? 'N'
    : modifiedAttackVector === 'Adjacent'
      ? 'A'
      : modifiedAttackVector === 'Local'
        ? 'L'
        : modifiedAttackVector === 'Physical'
          ? 'P'
          : baseAttackVector
}

function resolveModifiedAttackComplexity(
  baseAttackComplexity: CvssAttackComplexity,
  modifiedAttackComplexity: CvssModifiedAttackComplexity,
): CvssAttackComplexity {
  return modifiedAttackComplexity === 'Low'
    ? 'L'
    : modifiedAttackComplexity === 'High'
      ? 'H'
      : baseAttackComplexity
}

function resolveModifiedPrivilegesRequired(
  basePrivilegesRequired: CvssPrivilegesRequired,
  modifiedPrivilegesRequired: CvssModifiedPrivilegesRequired,
): CvssPrivilegesRequired {
  return modifiedPrivilegesRequired === 'None'
    ? 'N'
    : modifiedPrivilegesRequired === 'Low'
      ? 'L'
      : modifiedPrivilegesRequired === 'High'
        ? 'H'
        : basePrivilegesRequired
}

function resolveModifiedUserInteraction(
  baseUserInteraction: CvssUserInteraction,
  modifiedUserInteraction: CvssModifiedUserInteraction,
): CvssUserInteraction {
  return modifiedUserInteraction === 'None'
    ? 'N'
    : modifiedUserInteraction === 'Required'
      ? 'R'
      : baseUserInteraction
}

function resolveModifiedScope(baseScope: CvssScope, modifiedScope: CvssModifiedScope): CvssScope {
  return modifiedScope === 'Unchanged'
    ? 'U'
    : modifiedScope === 'Changed'
      ? 'C'
      : baseScope
}

function resolveModifiedImpact(baseImpact: CvssImpact, modifiedImpact: CvssModifiedImpact): CvssImpact {
  return modifiedImpact === 'None'
    ? 'N'
    : modifiedImpact === 'Low'
      ? 'L'
      : modifiedImpact === 'High'
        ? 'H'
        : baseImpact
}

function isAttackVector(value: string | undefined): value is CvssAttackVector {
  return value === 'N' || value === 'A' || value === 'L' || value === 'P'
}

function isAttackComplexity(value: string | undefined): value is CvssAttackComplexity {
  return value === 'L' || value === 'H'
}

function isPrivilegesRequired(value: string | undefined): value is CvssPrivilegesRequired {
  return value === 'N' || value === 'L' || value === 'H'
}

function isUserInteraction(value: string | undefined): value is CvssUserInteraction {
  return value === 'N' || value === 'R'
}

function isScope(value: string | undefined): value is CvssScope {
  return value === 'U' || value === 'C'
}

function isImpact(value: string | undefined): value is CvssImpact {
  return value === 'N' || value === 'L' || value === 'H'
}
