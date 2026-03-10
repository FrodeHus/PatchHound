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
