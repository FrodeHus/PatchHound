import type { SecurityProfile } from '@/api/security-profiles.schemas'
import type {
  securityProfileEnvironmentClassOptions,
  securityProfileInternetReachabilityOptions,
  securityProfileModifiedAttackComplexityOptions,
  securityProfileModifiedAttackVectorOptions,
  securityProfileModifiedImpactOptions,
  securityProfileModifiedPrivilegesRequiredOptions,
  securityProfileModifiedScopeOptions,
  securityProfileModifiedUserInteractionOptions,
  securityProfileRequirementOptions,
} from '@/lib/options/security-profiles'

export type SecurityProfileDraft = {
  name: string
  description: string
  environmentClass: (typeof securityProfileEnvironmentClassOptions)[number]
  internetReachability: (typeof securityProfileInternetReachabilityOptions)[number]
  confidentialityRequirement: (typeof securityProfileRequirementOptions)[number]
  integrityRequirement: (typeof securityProfileRequirementOptions)[number]
  availabilityRequirement: (typeof securityProfileRequirementOptions)[number]
  modifiedAttackVector: (typeof securityProfileModifiedAttackVectorOptions)[number]
  modifiedAttackComplexity: (typeof securityProfileModifiedAttackComplexityOptions)[number]
  modifiedPrivilegesRequired: (typeof securityProfileModifiedPrivilegesRequiredOptions)[number]
  modifiedUserInteraction: (typeof securityProfileModifiedUserInteractionOptions)[number]
  modifiedScope: (typeof securityProfileModifiedScopeOptions)[number]
  modifiedConfidentialityImpact: (typeof securityProfileModifiedImpactOptions)[number]
  modifiedIntegrityImpact: (typeof securityProfileModifiedImpactOptions)[number]
  modifiedAvailabilityImpact: (typeof securityProfileModifiedImpactOptions)[number]
}

export function createSecurityProfileDraft(profile?: SecurityProfile | null): SecurityProfileDraft {
  if (profile) {
    return {
      name: profile.name,
      description: profile.description ?? '',
      environmentClass: profile.environmentClass as SecurityProfileDraft['environmentClass'],
      internetReachability: profile.internetReachability as SecurityProfileDraft['internetReachability'],
      confidentialityRequirement: profile.confidentialityRequirement as SecurityProfileDraft['confidentialityRequirement'],
      integrityRequirement: profile.integrityRequirement as SecurityProfileDraft['integrityRequirement'],
      availabilityRequirement: profile.availabilityRequirement as SecurityProfileDraft['availabilityRequirement'],
      modifiedAttackVector: profile.modifiedAttackVector as SecurityProfileDraft['modifiedAttackVector'],
      modifiedAttackComplexity: profile.modifiedAttackComplexity as SecurityProfileDraft['modifiedAttackComplexity'],
      modifiedPrivilegesRequired: profile.modifiedPrivilegesRequired as SecurityProfileDraft['modifiedPrivilegesRequired'],
      modifiedUserInteraction: profile.modifiedUserInteraction as SecurityProfileDraft['modifiedUserInteraction'],
      modifiedScope: profile.modifiedScope as SecurityProfileDraft['modifiedScope'],
      modifiedConfidentialityImpact: profile.modifiedConfidentialityImpact as SecurityProfileDraft['modifiedConfidentialityImpact'],
      modifiedIntegrityImpact: profile.modifiedIntegrityImpact as SecurityProfileDraft['modifiedIntegrityImpact'],
      modifiedAvailabilityImpact: profile.modifiedAvailabilityImpact as SecurityProfileDraft['modifiedAvailabilityImpact'],
    }
  }

  return {
    name: '',
    description: '',
    environmentClass: 'Server',
    internetReachability: 'Internet',
    confidentialityRequirement: 'Medium',
    integrityRequirement: 'Medium',
    availabilityRequirement: 'Medium',
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

export function securityProfilePayload(draft: SecurityProfileDraft) {
  return {
    name: draft.name.trim(),
    description: draft.description.trim() || undefined,
    environmentClass: draft.environmentClass,
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
