export const securityProfileEnvironmentClassOptions = [
  'Workstation',
  'Server',
  'JumpHost',
  'Lab',
  'Kiosk',
  'OT',
] as const

export const securityProfileInternetReachabilityOptions = [
  'Internet',
  'InternalNetwork',
  'AdjacentOnly',
  'LocalOnly',
] as const

export const securityProfileRequirementOptions = ['Low', 'Medium', 'High'] as const
export const securityProfileModifiedAttackVectorOptions = ['NotDefined', 'Network', 'Adjacent', 'Local', 'Physical'] as const
export const securityProfileModifiedAttackComplexityOptions = ['NotDefined', 'Low', 'High'] as const
export const securityProfileModifiedPrivilegesRequiredOptions = ['NotDefined', 'None', 'Low', 'High'] as const
export const securityProfileModifiedUserInteractionOptions = ['NotDefined', 'None', 'Required'] as const
export const securityProfileModifiedScopeOptions = ['NotDefined', 'Unchanged', 'Changed'] as const
export const securityProfileModifiedImpactOptions = ['NotDefined', 'None', 'Low', 'High'] as const

export const securityProfileFieldGuidance = {
  environmentClass: {
    label: 'Environment Class',
    description: 'Use this to describe the device role. It helps people understand why the profile exists and what kind of endpoint it should be assigned to.',
  },
  internetReachability: {
    label: 'Internet Reachability',
    description: 'This affects exploitability. Devices reachable from the internet should keep more severe network-based exposure than devices limited to internal, adjacent, or local access.',
  },
  confidentialityRequirement: {
    label: 'Confidentiality Requirement',
    description: 'Raise this when data disclosure matters more for this device. Higher values increase the impact of vulnerabilities that expose data.',
  },
  integrityRequirement: {
    label: 'Integrity Requirement',
    description: 'Raise this when unauthorized changes would be especially harmful. Higher values increase the impact of tampering-oriented vulnerabilities.',
  },
  availabilityRequirement: {
    label: 'Availability Requirement',
    description: 'Raise this when uptime matters. Higher values increase the impact of denial-of-service or outage-causing vulnerabilities.',
  },
  modifiedAttackVector: {
    label: 'Modified Attack Vector',
    description: 'Authoritative environmental override for how reachable exploitation is in this environment.',
  },
  modifiedAttackComplexity: {
    label: 'Modified Attack Complexity',
    description: 'Override exploit preconditions when this environment materially changes the difficulty of exploitation.',
  },
  modifiedPrivilegesRequired: {
    label: 'Modified Privileges Required',
    description: 'Override how much privilege is realistically required in this environment.',
  },
  modifiedUserInteraction: {
    label: 'Modified User Interaction',
    description: 'Override whether another user action is still required in this environment.',
  },
  modifiedScope: {
    label: 'Modified Scope',
    description: 'Override whether exploitation can still cross security boundaries in this environment.',
  },
  modifiedConfidentialityImpact: {
    label: 'Modified Confidentiality Impact',
    description: 'Authoritative environmental override for confidentiality impact.',
  },
  modifiedIntegrityImpact: {
    label: 'Modified Integrity Impact',
    description: 'Authoritative environmental override for integrity impact.',
  },
  modifiedAvailabilityImpact: {
    label: 'Modified Availability Impact',
    description: 'Authoritative environmental override for availability impact.',
  },
} as const

export const securityProfileInternetReachabilityHelp: Record<(typeof securityProfileInternetReachabilityOptions)[number], string> = {
  Internet: 'Use for externally reachable systems. Network-exploitable vulnerabilities stay highly exposed.',
  InternalNetwork: 'Use for assets only reachable inside your organization. This still allows network exposure, but removes direct internet reachability.',
  AdjacentOnly: 'Use for segmented or same-network access only. This reduces exposure for broader network attack paths.',
  LocalOnly: 'Use for tightly isolated devices that require local presence or an already established foothold.',
}

export const securityProfileRequirementHelp: Record<(typeof securityProfileRequirementOptions)[number], string> = {
  Low: 'The business impact is lower for this dimension, so PatchHound reduces how much this factor increases severity.',
  Medium: 'Balanced default. Use when this device does not need special weighting.',
  High: 'The business impact is high for this dimension, so PatchHound increases how much this factor affects severity.',
}

export const securityProfileEnvironmentHelp: Record<(typeof securityProfileEnvironmentClassOptions)[number], string> = {
  Workstation: 'General user endpoint profile.',
  Server: 'Service-hosting system where confidentiality, integrity, or uptime may matter more.',
  JumpHost: 'Access broker or admin system with elevated exposure and importance.',
  Lab: 'Test or isolated environment where some impact dimensions may be lower.',
  Kiosk: 'Locked-down interactive endpoint with constrained user behavior.',
  OT: 'Operational technology or production control environment where availability often matters more.',
}

export const securityProfileModifiedAttackVectorHelp: Record<(typeof securityProfileModifiedAttackVectorOptions)[number], string> = {
  NotDefined: 'Use the vendor CVSS attack vector as-is.',
  Network: 'Treat exploitation as possible over the network.',
  Adjacent: 'Treat exploitation as limited to adjacent network conditions.',
  Local: 'Treat exploitation as requiring local or logged-in access.',
  Physical: 'Treat exploitation as requiring physical access.',
}

export const securityProfileModifiedAttackComplexityHelp: Record<(typeof securityProfileModifiedAttackComplexityOptions)[number], string> = {
  NotDefined: 'Use the vendor CVSS attack complexity as-is.',
  Low: 'Treat exploitation as requiring no uncommon conditions.',
  High: 'Treat exploitation as dependent on difficult or uncommon conditions.',
}

export const securityProfileModifiedPrivilegesRequiredHelp: Record<(typeof securityProfileModifiedPrivilegesRequiredOptions)[number], string> = {
  NotDefined: 'Use the vendor CVSS privileges-required metric as-is.',
  None: 'Treat exploitation as requiring no prior privileges.',
  Low: 'Treat exploitation as requiring basic privileges.',
  High: 'Treat exploitation as requiring elevated privileges.',
}

export const securityProfileModifiedUserInteractionHelp: Record<(typeof securityProfileModifiedUserInteractionOptions)[number], string> = {
  NotDefined: 'Use the vendor CVSS user-interaction metric as-is.',
  None: 'Treat exploitation as requiring no user interaction.',
  Required: 'Treat exploitation as still requiring another user action.',
}

export const securityProfileModifiedScopeHelp: Record<(typeof securityProfileModifiedScopeOptions)[number], string> = {
  NotDefined: 'Use the vendor CVSS scope as-is.',
  Unchanged: 'Treat impact as remaining within the same security scope.',
  Changed: 'Treat impact as crossing a security boundary.',
}

export const securityProfileModifiedImpactHelp: Record<(typeof securityProfileModifiedImpactOptions)[number], string> = {
  NotDefined: 'Use the vendor CVSS impact metric as-is.',
  None: 'Treat this impact dimension as unaffected in this environment.',
  Low: 'Treat this impact dimension as limited.',
  High: 'Treat this impact dimension as high.',
}
