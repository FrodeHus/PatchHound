import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'
import { remediationTaskSummarySchema } from './remediation-tasks.schemas'

// Phase 1 canonical cleanup (Task 15): schemas for the device-native
// /api/devices surface. Fields that previously lived on the legacy
// AssetDetailDto but are still keyed to the Asset navigation
// (vulnerabilities, softwareInventory, knownSoftwareVulnerabilities,
// softwareCpeBinding, tenantSoftwareId) are intentionally omitted —
// Phase 5 will rewire those tables and restore the sections.

const businessLabelSummarySchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  color: z.string().nullable(),
})

export const deviceSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  name: z.string(),
  currentRiskScore: z.number().nullable(),
  groupName: z.string().nullable(),
  criticality: z.string(),
  ownerType: z.string(),
  ownerUserId: z.string().uuid().nullable(),
  ownerTeamId: z.string().uuid().nullable(),
  securityProfileName: z.string().nullable(),
  vulnerabilityCount: z.number(),
  recurringVulnerabilityCount: z.number(),
  healthStatus: z.string().nullable(),
  riskScore: z.string().nullable(),
  exposureLevel: z.string().nullable(),
  tags: z.array(z.string()),
  businessLabels: z.array(businessLabelSummarySchema),
  onboardingStatus: z.string().nullable(),
  deviceValue: z.string().nullable(),
})

export const deviceDetailSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  name: z.string(),
  description: z.string().nullable(),
  criticality: z.string(),
  criticalityDetail: z
    .object({
      source: z.string(),
      reason: z.string().nullable(),
      ruleId: z.string().uuid().nullable(),
      updatedAt: z.string().datetime({ offset: true }).nullable(),
    })
    .nullable(),
  ownerType: z.string(),
  ownerUserName: z.string().nullable(),
  ownerUserId: z.string().uuid().nullable(),
  ownerTeamName: z.string().nullable(),
  ownerTeamId: z.string().uuid().nullable(),
  fallbackTeamName: z.string().nullable(),
  fallbackTeamId: z.string().uuid().nullable(),
  securityProfile: z
    .object({
      id: z.string().uuid(),
      name: z.string(),
      environmentClass: z.string(),
      internetReachability: z.string(),
      confidentialityRequirement: z.string(),
      integrityRequirement: z.string(),
      availabilityRequirement: z.string(),
    })
    .nullable(),
  computerDnsName: z.string().nullable(),
  healthStatus: z.string().nullable(),
  osPlatform: z.string().nullable(),
  osVersion: z.string().nullable(),
  riskScore: z.string().nullable(),
  lastSeenAt: z.string().nullable(),
  lastIpAddress: z.string().nullable(),
  aadDeviceId: z.string().nullable(),
  groupId: z.string().nullable(),
  groupName: z.string().nullable(),
  exposureLevel: z.string().nullable(),
  isAadJoined: z.boolean().nullable(),
  onboardingStatus: z.string().nullable(),
  deviceValue: z.string().nullable(),
  businessLabels: z.array(businessLabelSummarySchema),
  risk: z
    .object({
      overallScore: z.number(),
      maxEpisodeRiskScore: z.number(),
      riskBand: z.string(),
      openEpisodeCount: z.number(),
      criticalCount: z.number(),
      highCount: z.number(),
      mediumCount: z.number(),
      lowCount: z.number(),
      calculatedAt: z.string(),
    })
    .nullable(),
  remediation: remediationTaskSummarySchema.nullable(),
  tags: z.array(z.string()),
  metadata: z.string(),
})

export const pagedDevicesSchema = pagedResponseMetaSchema.extend({
  items: z.array(deviceSchema),
})

export const deviceExposureSchema = z.object({
  exposureId: z.string().uuid(),
  vulnerabilityId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  severity: z.string(),
  matchedVersion: z.string(),
  matchSource: z.string(),
  status: z.string(),
  firstObservedAt: z.string().datetime({ offset: true }),
  lastObservedAt: z.string().datetime({ offset: true }),
  resolvedAt: z.string().datetime({ offset: true }).nullable(),
  environmentalCvss: z.number().nullable(),
})

export const pagedDeviceExposuresSchema = pagedResponseMetaSchema.extend({
  items: z.array(deviceExposureSchema),
})

export type Device = z.infer<typeof deviceSchema>
export type DeviceDetail = z.infer<typeof deviceDetailSchema>
export type PagedDevices = z.infer<typeof pagedDevicesSchema>
export type DeviceExposure = z.infer<typeof deviceExposureSchema>
export type PagedDeviceExposures = z.infer<typeof pagedDeviceExposuresSchema>
