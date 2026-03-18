# Onboarding Status: Persist, Filter, and Dashboard Metric

## Problem

The Defender API returns `onboardingStatus` for machines, but PatchHound does not capture it. Devices with `InsufficientInfo` status should be skipped during ingestion. The field should be surfaced in the asset list/detail and as a dashboard metric card.

## Decisions

- **InsufficientInfo handling**: Skip ingestion entirely — don't create/update the Asset. If a previously onboarded device later reports `InsufficientInfo`, leave the existing record as-is.
- **Dashboard metric**: Not affected by vulnerability filters (same as device health — this is a device inventory metric).

---

## 1. Data Model

### New column on Asset entity

| Column | Type | Notes |
|---|---|---|
| `DeviceOnboardingStatus` | varchar(64), nullable | "Onboarded", "InsufficientInfo", "CanBeOnboarded", etc. |

### Ingestion pipeline

- Add `OnboardingStatus` (string?) to `DefenderMachineEntry`.
- Add `DeviceOnboardingStatus` (string?) to `IngestionAsset`.
- `DefenderVulnerabilitySource.NormalizeMachineAsset` returns `null` when `OnboardingStatus == "InsufficientInfo"`. Caller filters out nulls.
- `Asset.UpdateDeviceDetails()` gains `onboardingStatus` parameter.
- `StagedAssetMergeService` passes through to both new-asset and update-asset paths.

---

## 2. API Changes

### DashboardSummaryDto — new field

- `DeviceOnboardingBreakdown: Dictionary<string, int>` — grouped by `DeviceOnboardingStatus`, same query pattern as `DeviceHealthBreakdown`. Not affected by vulnerability filters.

### AssetDto (list) — new field

- `onboardingStatus` (string?)

### AssetDetailDto (detail) — new field

- `deviceOnboardingStatus` (string?)

### AssetFilterQuery — new filter

- `OnboardingStatus` (string?) — exact match

---

## 3. Frontend Changes

### Dashboard

- New `OnboardingStatusCard.tsx` — same pattern as `DeviceHealthCard`. Shows "Onboarded" count vs total, clickable rows for non-Onboarded statuses linking to `/assets?onboardingStatus=<value>`.
- Added to dashboard route alongside DeviceHealthCard.

### Asset list table

- New "Onboarding" column.
- New filter control for onboarding status.

### Asset detail (DeviceSection)

- New "Onboarding Status" field in device info grid.

### Schemas & server functions

- Update `dashboard.schemas.ts` with `deviceOnboardingBreakdown`.
- Update `assets.schemas.ts` with new fields on list and detail schemas.
- Update `assets.functions.ts` to pass new filter parameter.

---

## Summary of Changes

| Layer | Change |
|---|---|
| DefenderMachineEntry | Add `OnboardingStatus` field |
| IngestionAsset | Add `DeviceOnboardingStatus` field |
| DefenderVulnerabilitySource | Skip `InsufficientInfo` machines, pass field through |
| Asset entity | Add `DeviceOnboardingStatus` property |
| StagedAssetMergeService | Pass onboarding status through UpdateDeviceDetails |
| EF Migration | Add `DeviceOnboardingStatus` column |
| DashboardController | Add onboarding breakdown query |
| DashboardSummaryDto | Add `DeviceOnboardingBreakdown` field |
| AssetDto / AssetDetailDto | Add onboarding status fields |
| AssetFilterQuery | Add `OnboardingStatus` filter |
| Dashboard frontend | New OnboardingStatusCard component |
| Asset list frontend | New column + filter |
| Asset detail frontend | New field in DeviceSection |
