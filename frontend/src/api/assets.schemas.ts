import { z } from 'zod'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const assetSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  name: z.string(),
  assetType: z.string(),
  deviceGroupName: z.string().nullable(),
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
  onboardingStatus: z.string().nullable(),
  deviceValue: z.string().nullable(),
});

export const assetVulnerabilitySchema = z.object({
  vulnerabilityId: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  description: z.string(),
  vendorSeverity: z.string(),
  vendorScore: z.number().nullable(),
  cvssVector: z.string().nullable(),
  publishedDate: z.string().nullable(),
  effectiveSeverity: z.string(),
  effectiveScore: z.number().nullable(),
  assessmentReasonSummary: z.string().nullable(),
  status: z.string(),
  detectedDate: z.string(),
  resolvedDate: z.string().nullable(),
  episodeCount: z.number(),
  episodes: z.array(z.object({
    episodeNumber: z.number(),
    status: z.string(),
    firstSeenAt: z.string(),
    lastSeenAt: z.string(),
    resolvedAt: z.string().nullable(),
  })),
  possibleCorrelatedSoftware: z.array(z.string()),
})

export const assetDetailSchema = z.object({
  id: z.string().uuid(),
  tenantSoftwareId: z.string().uuid().nullable(),
  externalId: z.string(),
  name: z.string(),
  description: z.string().nullable(),
  assetType: z.string(),
  criticality: z.string(),
  ownerType: z.string(),
  ownerUserId: z.string().uuid().nullable(),
  ownerTeamId: z.string().uuid().nullable(),
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
  deviceComputerDnsName: z.string().nullable(),
  deviceHealthStatus: z.string().nullable(),
  deviceOsPlatform: z.string().nullable(),
  deviceOsVersion: z.string().nullable(),
  deviceRiskScore: z.string().nullable(),
  deviceLastSeenAt: z.string().nullable(),
  deviceLastIpAddress: z.string().nullable(),
  deviceAadDeviceId: z.string().nullable(),
  deviceGroupId: z.string().nullable(),
  deviceGroupName: z.string().nullable(),
  deviceExposureLevel: z.string().nullable(),
  deviceIsAadJoined: z.boolean().nullable(),
  deviceOnboardingStatus: z.string().nullable(),
  deviceValue: z.string().nullable(),
  tags: z.array(z.string()),
  softwareCpeBinding: z
    .object({
      id: z.string().uuid(),
      cpe23Uri: z.string(),
      bindingMethod: z.string(),
      confidence: z.string(),
      matchedVendor: z.string().nullable(),
      matchedProduct: z.string().nullable(),
      matchedVersion: z.string().nullable(),
      lastValidatedAt: z.string(),
    })
    .nullable(),
  metadata: z.string(),
  vulnerabilities: z.array(assetVulnerabilitySchema),
  softwareInventory: z.array(
    z.object({
      softwareAssetId: z.string().uuid(),
      tenantSoftwareId: z.string().uuid().nullable(),
      name: z.string(),
      externalId: z.string(),
      lastSeenAt: z.string(),
      cpeBinding: z
        .object({
          id: z.string().uuid(),
          cpe23Uri: z.string(),
          bindingMethod: z.string(),
          confidence: z.string(),
          matchedVendor: z.string().nullable(),
          matchedProduct: z.string().nullable(),
          matchedVersion: z.string().nullable(),
          lastValidatedAt: z.string(),
        })
        .nullable(),
      episodeCount: z.number(),
      episodes: z.array(
        z.object({
          episodeNumber: z.number(),
          firstSeenAt: z.string(),
          lastSeenAt: z.string(),
          removedAt: z.string().nullable(),
        }),
      ),
    }),
  ),
  knownSoftwareVulnerabilities: z.array(
    z.object({
      vulnerabilityId: z.string().uuid(),
      externalId: z.string(),
      title: z.string(),
      vendorSeverity: z.string(),
      cvssScore: z.number().nullable(),
      cvssVector: z.string().nullable(),
      matchMethod: z.string(),
      confidence: z.string(),
      evidence: z.string(),
      firstSeenAt: z.string(),
      lastSeenAt: z.string(),
      resolvedAt: z.string().nullable(),
    }),
  ),
});

export const pagedAssetsSchema = pagedResponseMetaSchema.extend({
  items: z.array(assetSchema),
})

export type Asset = z.infer<typeof assetSchema>
export type AssetDetail = z.infer<typeof assetDetailSchema>
export type PagedAssets = z.infer<typeof pagedAssetsSchema>
